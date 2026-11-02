using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LojaVirtual.Models;
using LojaVirtual.Services;
using Microsoft.Extensions.Logging;

namespace LojaVirtual.Web.Services;

public sealed class MercadoPagoService : IMercadoPagoService
{
    private const string ApiBaseUrl = "https://api.mercadopago.com";
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MercadoPagoService>? _logger;

    public MercadoPagoService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<MercadoPagoService>? logger = null)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<MercadoPagoPreferenceResult> CreatePreferenceAsync(
        Order order,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var accessToken = Required("MercadoPago:AccessToken");
        var storefrontUrl = RequiredPublicHttpsUrl("Storefront:BaseUrl");
        var notificationUrl = RequiredPublicHttpsUrl("MercadoPago:WebhookUrl");
        var expirationMinutes = _configuration.GetValue("MercadoPago:PreferenceExpirationMinutes", 30);
        var startsAt = DateTimeOffset.UtcNow;
        var attemptExpiration = order.PaymentAttempts
            .FirstOrDefault(attempt => string.Equals(attempt.IdempotencyKey, idempotencyKey, StringComparison.Ordinal))
            ?.ExpiresAtUtc;
        var expiresAt = attemptExpiration is { } persistedExpiration
            ? new DateTimeOffset(DateTime.SpecifyKind(persistedExpiration, DateTimeKind.Utc))
            : startsAt.AddMinutes(expirationMinutes);
        if (expiresAt <= startsAt)
        {
            throw new MercadoPagoException("A preferência não pode ser criada após a expiração da reserva de estoque.");
        }

        var items = order.Items.Select(item => new
        {
            id = item.ProductId.ToString(CultureInfo.InvariantCulture),
            title = item.Product?.Name ?? $"Produto #{item.ProductId}",
            quantity = item.Quantity,
            currency_id = "BRL",
            unit_price = item.UnitPrice
        }).Cast<object>().ToList();

        var shipping = order.Total - order.Items.Sum(item => item.UnitPrice * item.Quantity);
        if (shipping > 0)
        {
            items.Add(new
            {
                id = $"frete-{order.Id}",
                title = $"Frete {order.ShippingMethod}",
                quantity = 1,
                currency_id = "BRL",
                unit_price = shipping
            });
        }

        var body = new
        {
            items,
            payer = new { email = order.Email },
            external_reference = order.Id.ToString(CultureInfo.InvariantCulture),
            notification_url = notificationUrl,
            back_urls = new
            {
                success = $"{storefrontUrl}/checkout?orderId={order.Id}&returnStatus=success",
                failure = $"{storefrontUrl}/checkout?orderId={order.Id}&returnStatus=failure",
                pending = $"{storefrontUrl}/checkout?orderId={order.Id}&returnStatus=pending"
            },
            auto_return = "approved",
            expires = true,
            expiration_date_from = FormatPreferenceDate(startsAt),
            expiration_date_to = FormatPreferenceDate(expiresAt)
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/checkout/preferences")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add(
            "X-Idempotency-Key",
            idempotencyKey ?? $"angel-order-{order.Id}-attempt-{order.PaymentAttempts.Count + 1}");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var details = ParseApiError(responseBody);
            var requestId = response.Headers.TryGetValues("x-request-id", out var requestIds)
                ? requestIds.FirstOrDefault()
                : null;
            _logger?.LogWarning(
                "Mercado Pago rejeitou preferência. StatusCode={StatusCode}; RequestId={RequestId}; " +
                "ExpiresValue={ExpiresValue}; ExpiresJsonType={ExpiresJsonType}; " +
                "ExpirationDateFrom={ExpirationDateFrom}; ExpirationDateFromJsonType={ExpirationDateFromJsonType}; " +
                "ExpirationDateTo={ExpirationDateTo}; ExpirationDateToJsonType={ExpirationDateToJsonType}; " +
                "ErrorCode={ErrorCode}; ErrorMessage={ErrorMessage}; Causes={Causes}",
                (int)response.StatusCode,
                SafeLogValue(requestId),
                true,
                "Boolean",
                FormatPreferenceDate(startsAt),
                "String",
                FormatPreferenceDate(expiresAt),
                "String",
                details.Code,
                details.Message,
                details.Causes.Count == 0 ? null : string.Join(" | ", details.Causes));
            throw new MercadoPagoException(
                $"Mercado Pago rejeitou a preferência ({(int)response.StatusCode}): {details.ToMessage()}");
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        var preferenceId = root.GetProperty("id").GetString();
        var initPoint = root.GetProperty("init_point").GetString();
        if (string.IsNullOrWhiteSpace(preferenceId) || string.IsNullOrWhiteSpace(initPoint))
        {
            throw new MercadoPagoException("Mercado Pago retornou uma preferência sem URL de checkout.");
        }

        return new MercadoPagoPreferenceResult(preferenceId, initPoint);
    }

    public async Task<string> CriarPreferenciaAsync(Order order, string? idempotencyKey = null) =>
        (await CreatePreferenceAsync(order, idempotencyKey)).InitPoint;

    public async Task<MercadoPagoPaymentResult?> GetPaymentAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(paymentId) || paymentId.Length > 64) return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/v1/payments/{Uri.EscapeDataString(paymentId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Required("MercadoPago:AccessToken"));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw new MercadoPagoException("Não foi possível consultar o pagamento no Mercado Pago.");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        return new MercadoPagoPaymentResult(
            root.GetProperty("id").ToString(),
            root.GetProperty("status").GetString() ?? "unknown",
            GetString(root, "external_reference"),
            GetString(root, "preference_id"),
            root.TryGetProperty("transaction_amount", out var amount) ? amount.GetDecimal() : 0m,
            GetString(root, "currency_id") ?? string.Empty,
            GetDate(root, "date_created"),
            GetDate(root, "date_last_updated"));
    }

    public bool IsValidWebhookSignature(string? signature, string? requestId, string? dataId)
    {
        var secret = _configuration["MercadoPago:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(signature) ||
            string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(dataId)) return false;

        var values = signature.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);
        if (!values.TryGetValue("ts", out var timestamp) || !values.TryGetValue("v1", out var expected)) return false;
        if (!long.TryParse(timestamp, out var timestampSeconds)) return false;
        var signatureTime = DateTimeOffset.FromUnixTimeSeconds(timestampSeconds);
        if (Math.Abs((DateTimeOffset.UtcNow - signatureTime).TotalMinutes) > 5) return false;

        var manifest = $"id:{dataId};request-id:{requestId};ts:{timestamp};";
        var actual = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));
    }

    private string Required(string key) => _configuration[key] is { Length: > 0 } value
        ? value
        : throw new MercadoPagoException($"Configuração ausente: {key}.");

    private string RequiredUrl(string key, bool requireHttps = false)
    {
        var value = Required(key).TrimEnd('/');
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (requireHttps && uri.Scheme != Uri.UriSchemeHttps))
            throw new MercadoPagoException($"Configuração inválida: {key}.");
        return value;
    }

    private string RequiredPublicHttpsUrl(string key)
    {
        var value = RequiredUrl(key);
        var uri = new Uri(value);
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            (IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address)))
        {
            throw new MercadoPagoException(
                $"Configuração inválida: {key} deve usar uma URL HTTPS pública; localhost não é aceito pelo Mercado Pago.");
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new MercadoPagoException($"Configuração inválida: {key}.");
        }

        return value;
    }

    private static MercadoPagoApiError ParseApiError(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            var code = GetString(root, "error");
            var message = GetString(root, "message");
            var causes = root.TryGetProperty("cause", out var causeElement) &&
                         causeElement.ValueKind == JsonValueKind.Array
                ? causeElement.EnumerateArray()
                    .Select(cause =>
                    {
                        var causeCode = GetString(cause, "code");
                        var description = GetString(cause, "description");
                        return string.Join(": ", new[] { causeCode, description }
                            .Where(value => !string.IsNullOrWhiteSpace(value)));
                    })
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Take(3)
                    .ToArray()
                : [];

            return new MercadoPagoApiError(
                SafeLogValue(code),
                SafeLogValue(message),
                causes.Select(SafeLogValue).Where(value => value is not null).Cast<string>().ToArray());
        }
        catch (JsonException)
        {
            return new MercadoPagoApiError(null, "a API retornou uma resposta de erro não estruturada", []);
        }
    }

    private static string? SafeLogValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Replace('\r', ' ').Replace('\n', ' ');
        return normalized[..Math.Min(normalized.Length, 400)];
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null ? property.ToString() : null;

    private static string FormatPreferenceDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);

    private static DateTimeOffset? GetDate(JsonElement root, string name) =>
        DateTimeOffset.TryParse(GetString(root, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value) ? value : null;

    private sealed record MercadoPagoApiError(
        string? Code,
        string? Message,
        IReadOnlyList<string> Causes)
    {
        public string ToMessage()
        {
            var parts = new[]
            {
                Code,
                Message,
                Causes.Count == 0 ? null : string.Join(" | ", Causes)
            }.Where(value => !string.IsNullOrWhiteSpace(value));
            var result = string.Join("; ", parts);
            return string.IsNullOrWhiteSpace(result)
                ? "a API não informou detalhes"
                : result[..Math.Min(result.Length, 800)];
        }
    }
}

public sealed class MercadoPagoException : Exception
{
    public MercadoPagoException(string message) : base(message) { }
}
