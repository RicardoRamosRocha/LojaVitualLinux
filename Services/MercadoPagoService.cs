using System;
using System.Text;
using System.Text.Json;
using LojaVirtual.Models;
using LojaVirtual.Web.Services;

namespace LojaVirtual.Web.Services;

public class MercadoPagoService : IMercadoPagoService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public MercadoPagoService(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<string> CriarPreferenciaAsync(Order order)
    {
        var accessToken =
            _configuration["MercadoPago:AccessToken"];

        _httpClient.DefaultRequestHeaders.Clear();

        _httpClient.DefaultRequestHeaders.Add(
            "Authorization",
            $"Bearer {accessToken}");

        var body = new
        {
            items = new[]
            {
            new
            {
                id = order.Id.ToString(),
                title = $"Pedido #{order.Id}",
                quantity = 1,
                currency_id = "BRL",
                unit_price = order.Total
            }
        },

            back_urls = new
            {
                success = "https://localhost:5268/Checkout/Success",
                failure = "https://localhost:5268/Checkout/Failure",
                pending = "https://localhost:5268/Checkout/Pending"
            },

            auto_return = "approved",

            external_reference = order.Id.ToString()
        };

        var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync(
            "https://api.mercadopago.com/checkout/preferences",
            content);

        var responseContent =
            await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception(
                $"Erro Mercado Pago: {responseContent}");
        }

        using var document =
            JsonDocument.Parse(responseContent);

        return document.RootElement
            .GetProperty("init_point")
            .GetString()!;
    }


}