using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace LojaVirtual.Services;

public sealed class MelhorEnvioQuoteService : IShippingQuoteService
{
    private readonly HttpClient _httpClient;
    private readonly ShippingOptions _options;
    private readonly ILogger<MelhorEnvioQuoteService> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private string? _refreshToken;

    public MelhorEnvioQuoteService(
        HttpClient httpClient,
        IOptions<ShippingOptions> options,
        ILogger<MelhorEnvioQuoteService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _accessToken = NullIfWhiteSpace(_options.AccessToken);
        _refreshToken = NullIfWhiteSpace(_options.RefreshToken);
    }

    public async Task<ShippingQuoteResult> QuoteAsync(
        string destinationPostalCode,
        IReadOnlyList<ShippingQuoteItem> items,
        CancellationToken cancellationToken = default)
    {
        ValidateConfiguration(destinationPostalCode, items);

        var body = new
        {
            from = new { postal_code = NormalizePostalCode(_options.OriginPostalCode) },
            to = new { postal_code = NormalizePostalCode(destinationPostalCode) },
            volumes = new[]
            {
                new
                {
                    width = _options.StandardPackage.WidthCm,
                    height = _options.StandardPackage.HeightCm,
                    length = _options.StandardPackage.LengthCm,
                    weight = _options.StandardPackage.WeightKg,
                    insurance = items.Sum(item => item.UnitPrice * item.Quantity)
                }
            },
            options = new { receipt = false, own_hand = false },
            services = string.IsNullOrWhiteSpace(_options.Services) ? null : _options.Services
        };

        var accessToken = await GetAccessTokenAsync(false, cancellationToken);
        using var response = await SendQuoteRequestAsync(accessToken, body, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized && !string.IsNullOrWhiteSpace(_refreshToken))
        {
            accessToken = await GetAccessTokenAsync(true, cancellationToken);
            using var retryResponse = await SendQuoteRequestAsync(accessToken, body, cancellationToken);
            return await ParseQuoteResponseAsync(retryResponse, cancellationToken);
        }

        return await ParseQuoteResponseAsync(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendQuoteRequestAsync(
        string accessToken,
        object body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{RequiredBaseUrl()}/api/v2/me/shipment/calculate")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);

        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ShippingQuoteException("shipping_timeout", "A cotação de frete demorou para responder.", 504, exception);
        }
        catch (HttpRequestException exception)
        {
            throw new ShippingQuoteException("shipping_unavailable", "O serviço de frete está indisponível.", 503, exception);
        }
    }

    private async Task<ShippingQuoteResult> ParseQuoteResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Melhor Envio respondeu com HTTP {StatusCode} na cotação.", (int)response.StatusCode);
            var code = response.StatusCode == HttpStatusCode.UnprocessableEntity
                ? "shipping_invalid_request"
                : response.StatusCode == HttpStatusCode.Unauthorized
                    ? "shipping_unauthorized"
                    : "shipping_unavailable";
            throw new ShippingQuoteException(code, "Não foi possível obter opções de frete.", (int)response.StatusCode);
        }

        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new ShippingQuoteException("shipping_invalid_response", "A resposta do serviço de frete é inválida.");
        }

        var options = new List<ShippingQuoteOption>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("error", out var error) && error.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                continue;
            }

            var serviceId = ReadString(item, "id");
            var serviceName = ReadString(item, "name");
            var providerName = item.TryGetProperty("company", out var company)
                ? ReadString(company, "name")
                : string.Empty;
            var price = ReadDecimal(item, "custom_price") ?? ReadDecimal(item, "price");
            var deliveryTime = ReadInt(item, "custom_delivery_time") ?? ReadInt(item, "delivery_time");

            if (string.IsNullOrWhiteSpace(serviceId) || string.IsNullOrWhiteSpace(serviceName) || price is null || deliveryTime is null)
            {
                continue;
            }

            if (price < 0 || deliveryTime < 0) continue;
            options.Add(new ShippingQuoteOption(serviceId, serviceName, providerName, price.Value, deliveryTime.Value));
        }

        if (options.Count == 0)
        {
            throw new ShippingQuoteException("shipping_no_options", "Não há opções de frete disponíveis para este destino.", 422);
        }

        return new ShippingQuoteResult(options);
    }

    private async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && !string.IsNullOrWhiteSpace(_accessToken)) return _accessToken;
            if (string.IsNullOrWhiteSpace(_refreshToken))
            {
                throw new ShippingQuoteException("shipping_credentials_missing", "As credenciais do Melhor Envio não estão configuradas.", 503);
            }
            if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            {
                throw new ShippingQuoteException("shipping_credentials_missing", "Client ID e Client Secret do Melhor Envio não estão configurados.", 503);
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{RequiredBaseUrl()}/oauth/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["client_id"] = _options.ClientId,
                    ["client_secret"] = _options.ClientSecret,
                    ["refresh_token"] = _refreshToken
                })
            };
            request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new ShippingQuoteException("shipping_token_invalid", "Não foi possível renovar o token do Melhor Envio.", (int)response.StatusCode);
            }

            var token = await response.Content.ReadFromJsonAsync<MelhorEnvioTokenResponse>(cancellationToken: cancellationToken);
            if (string.IsNullOrWhiteSpace(token?.AccessToken))
            {
                throw new ShippingQuoteException("shipping_invalid_token_response", "O Melhor Envio retornou um token inválido.");
            }

            _accessToken = token.AccessToken;
            _refreshToken = NullIfWhiteSpace(token.RefreshToken) ?? _refreshToken;
            return _accessToken;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ShippingQuoteException("shipping_timeout", "A renovação do token do Melhor Envio demorou para responder.", 504, exception);
        }
        catch (HttpRequestException exception)
        {
            throw new ShippingQuoteException("shipping_unavailable", "O serviço de autenticação do Melhor Envio está indisponível.", 503, exception);
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void ValidateConfiguration(string destinationPostalCode, IReadOnlyList<ShippingQuoteItem> items)
    {
        if (!RegexPostalCode(destinationPostalCode) || !RegexPostalCode(_options.OriginPostalCode))
        {
            throw new ShippingQuoteException("shipping_invalid_postal_code", "Informe CEPs válidos para a cotação.", 400);
        }
        if (items.Count == 0 || items.Any(item => item.Quantity < 1 || item.UnitPrice < 0))
        {
            throw new ShippingQuoteException("shipping_invalid_items", "Os itens do carrinho não podem ser cotados.", 422);
        }
        var package = _options.StandardPackage;
        if (!package.Enabled || package.MaxItems < 1 || items.Sum(item => item.Quantity) > package.MaxItems)
        {
            throw new ShippingQuoteException("shipping_package_not_configured", "A embalagem padrão ainda não foi validada para este carrinho.", 422);
        }
        if (package.LengthCm <= 0 || package.WidthCm <= 0 || package.HeightCm <= 0 || package.WeightKg <= 0)
        {
            throw new ShippingQuoteException("shipping_package_invalid", "As dimensões e o peso da embalagem são inválidos.", 422);
        }
    }

    private string RequiredBaseUrl()
    {
        if (!Uri.TryCreate(_options.BaseUrl.TrimEnd('/'), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ShippingQuoteException("shipping_configuration_invalid", "A URL do Melhor Envio deve ser HTTPS.", 503);
        }
        return uri.ToString().TrimEnd('/');
    }

    private static bool RegexPostalCode(string value) => System.Text.RegularExpressions.Regex.IsMatch(value.Trim(), "^\\d{5}-?\\d{3}$");
    private static string NormalizePostalCode(string value) => new(value.Where(char.IsDigit).ToArray());
    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.ToString() : string.Empty;

    private static decimal? ReadDecimal(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    private static int? ReadInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    private sealed class MelhorEnvioTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }
    }
}
