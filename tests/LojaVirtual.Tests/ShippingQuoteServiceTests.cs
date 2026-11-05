using System.Net;
using LojaVirtual.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LojaVirtual.Tests;

public sealed class ShippingQuoteServiceTests
{
    [Fact]
    public async Task Parses_available_services_using_custom_price_and_delivery_time()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                [
                  {
                    "id": 1,
                    "name": "SEDEX",
                    "price": 38.50,
                    "custom_price": 35.00,
                    "delivery_time": 5,
                    "custom_delivery_time": 4,
                    "company": { "name": "Correios" }
                  },
                  { "id": 2, "name": "Indisponível", "error": "fora da área" }
                ]
                """)
        });
        var service = CreateService(handler);

        var result = await service.QuoteAsync("30110-012", [new ShippingQuoteItem(10, 2, 49.90m)]);

        var option = Assert.Single(result.Options);
        Assert.Equal("1", option.ServiceId);
        Assert.Equal("Correios", option.ProviderName);
        Assert.Equal(35.00m, option.Price);
        Assert.Equal(4, option.DeliveryTimeDays);
        Assert.Contains("shipment/calculate", handler.LastRequestUri);
        Assert.Contains("31615150", handler.LastRequestBody);
        Assert.Contains("30110012", handler.LastRequestBody);
    }

    [Fact]
    public async Task Blocks_when_standard_package_is_not_enabled_or_capacity_is_unknown()
    {
        var service = CreateService(new StubHandler(_ => throw new InvalidOperationException("API não deveria ser chamada")), maxItems: 0, packageEnabled: false);

        var exception = await Assert.ThrowsAsync<ShippingQuoteException>(() =>
            service.QuoteAsync("30110-012", [new ShippingQuoteItem(10, 1, 49.90m)]));

        Assert.Equal("shipping_package_not_configured", exception.Code);
    }

    [Fact]
    public async Task Maps_http_422_to_invalid_shipping_request()
    {
        var service = CreateService(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)));

        var exception = await Assert.ThrowsAsync<ShippingQuoteException>(() =>
            service.QuoteAsync("30110-012", [new ShippingQuoteItem(10, 1, 49.90m)]));

        Assert.Equal("shipping_invalid_request", exception.Code);
        Assert.Equal(422, exception.StatusCode);
    }

    private static MelhorEnvioQuoteService CreateService(
        HttpMessageHandler handler,
        int maxItems = 10,
        bool packageEnabled = true)
    {
        var client = new HttpClient(handler);
        var options = Options.Create(new ShippingOptions
        {
            RealQuotesEnabled = true,
            BaseUrl = "https://sandbox.melhorenvio.com.br",
            OriginPostalCode = "31615150",
            AccessToken = "test-token",
            UserAgent = "Angel Test (test@example.com)",
            StandardPackage = new StandardShippingPackage
            {
                Enabled = packageEnabled,
                MaxItems = maxItems,
                LengthCm = 16,
                WidthCm = 12,
                HeightCm = 5,
                WeightKg = 0.2m
            }
        });
        return new MelhorEnvioQuoteService(client, options, NullLogger<MelhorEnvioQuoteService>.Instance);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public string LastRequestBody { get; private set; } = string.Empty;
        public string LastRequestUri { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri?.ToString() ?? string.Empty;
            LastRequestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }
}
