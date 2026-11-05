namespace LojaVirtual.Services;

public sealed class ShippingOptions
{
    public bool RealQuotesEnabled { get; set; }
    public string BaseUrl { get; set; } = "https://sandbox.melhorenvio.com.br";
    public string OriginPostalCode { get; set; } = "31615150";
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string AuthorizationUrl { get; set; } = string.Empty;
    public string OAuthScope { get; set; } = string.Empty;
    public int OAuthStateLifetimeMinutes { get; set; } = 10;
    public string UserAgent { get; set; } = "Angel Acessórios (contato técnico)";
    public int QuoteTtlMinutes { get; set; } = 10;
    public string Services { get; set; } = string.Empty;
    public StandardShippingPackage StandardPackage { get; set; } = new();
}

public sealed class StandardShippingPackage
{
    public bool Enabled { get; set; }
    public decimal LengthCm { get; set; } = 16m;
    public decimal WidthCm { get; set; } = 12m;
    public decimal HeightCm { get; set; } = 5m;
    public decimal WeightKg { get; set; } = 0.2m;
    public int MaxItems { get; set; }
}

public sealed record ShippingQuoteItem(
    int ProductId,
    int Quantity,
    decimal UnitPrice);

public sealed record ShippingQuoteOption(
    string ServiceId,
    string ServiceName,
    string ProviderName,
    decimal Price,
    int DeliveryTimeDays);

public sealed record ShippingQuoteResult(
    IReadOnlyList<ShippingQuoteOption> Options);

public sealed class ShippingQuoteException : Exception
{
    public ShippingQuoteException(string code, string message, int statusCode = 503, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}

public interface IShippingQuoteService
{
    Task<ShippingQuoteResult> QuoteAsync(
        string destinationPostalCode,
        IReadOnlyList<ShippingQuoteItem> items,
        CancellationToken cancellationToken = default);
}

internal sealed class DisabledShippingQuoteService : IShippingQuoteService
{
    public Task<ShippingQuoteResult> QuoteAsync(
        string destinationPostalCode,
        IReadOnlyList<ShippingQuoteItem> items,
        CancellationToken cancellationToken = default) =>
        throw new ShippingQuoteException(
            "shipping_real_quotes_disabled",
            "A cotação real está desabilitada.",
            409);
}
