namespace MercadoPagoWebhookProxy;

public static class WebhookProxyPolicy
{
    public const string Path = "/api/payments/mercadopago/webhook";
    public const long MaxBodyBytes = 256 * 1024;

    public static bool IsAllowedPath(string path) =>
        string.Equals(path, Path, StringComparison.Ordinal);

    public static bool IsAllowedMethod(string method) =>
        string.Equals(method, HttpMethods.Post, StringComparison.OrdinalIgnoreCase);
}
