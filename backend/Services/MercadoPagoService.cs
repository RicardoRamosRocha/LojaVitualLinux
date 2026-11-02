using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LojaVirtual.Models;
using LojaVirtual.Services;

namespace LojaVirtual.Web.Services;

public sealed class MercadoPagoService : IMercadoPagoService
{
    private const string ApiBaseUrl = "https://api.mercadopago.com";
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public MercadoPagoService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<MercadoPagoPreferenceResult> CreatePreferenceAsync(Order order, CancellationToken cancellationToken = default)
    {
        var accessToken = Required("MercadoPago:AccessToken");
        var storefrontUrl = RequiredUrl("Storefront:BaseUrl");
        var notificationUrl = RequiredUrl("MercadoPago:WebhookUrl", requireHttps: true);
        var expirationMinutes = _configuration.GetValue("MercadoPago:PreferenceExpirationMinutes", 30);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(expirationMinutes);

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
            expiration_date_to = expiresAt.ToString("O", CultureInfo.InvariantCulture)
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/checkout/preferences")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("X-Idempotency-Key", $"angel-order-{order.Id}-{order.PaymentAttempts.Count + 1}");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new MercadoPagoException($"Mercado Pago rejeitou a preferência ({(int)response.StatusCode}).");
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

    public async Task<string> CriarPreferenciaAsync(Order order) =>
        (await CreatePreferenceAsync(order)).InitPoint;

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

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null ? property.ToString() : null;

    private static DateTimeOffset? GetDate(JsonElement root, string name) =>
        DateTimeOffset.TryParse(GetString(root, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value) ? value : null;
}

public sealed class MercadoPagoException : Exception
{
    public MercadoPagoException(string message) : base(message) { }
}
