using MercadoPagoWebhookProxy;
using Xunit;

namespace LojaVirtual.Tests;

public sealed class WebhookProxyPolicyTests
{
    [Fact]
    public void AllowsOnlyTheMercadoPagoWebhookPath()
    {
        Assert.True(WebhookProxyPolicy.IsAllowedPath(WebhookProxyPolicy.Path));
        Assert.False(WebhookProxyPolicy.IsAllowedPath("/admin"));
        Assert.False(WebhookProxyPolicy.IsAllowedPath("/api/products"));
        Assert.False(WebhookProxyPolicy.IsAllowedPath("/api/payments/mercadopago/webhook/"));
    }

    [Fact]
    public void AllowsOnlyPost()
    {
        Assert.True(WebhookProxyPolicy.IsAllowedMethod("POST"));
        Assert.False(WebhookProxyPolicy.IsAllowedMethod("GET"));
        Assert.False(WebhookProxyPolicy.IsAllowedMethod("PUT"));
        Assert.False(WebhookProxyPolicy.IsAllowedMethod("DELETE"));
    }

    [Fact]
    public void UsesBoundedRequestBodyLimit()
    {
        Assert.Equal(256 * 1024, WebhookProxyPolicy.MaxBodyBytes);
    }
}
