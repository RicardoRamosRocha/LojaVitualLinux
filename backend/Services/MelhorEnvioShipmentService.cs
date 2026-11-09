using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LojaVirtual.Data;
using LojaVirtual.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LojaVirtual.Services;

public sealed class ShippingOperationException : Exception
{
    public ShippingOperationException(string code, string message, int statusCode = 502, bool indeterminate = false, Exception? inner = null)
        : base(message, inner) { Code = code; StatusCode = statusCode; IsIndeterminate = indeterminate; }
    public string Code { get; }
    public int StatusCode { get; }
    public bool IsIndeterminate { get; }
}

public interface IMelhorEnvioShipmentService
{
    Task<OrderShipment> PrepareAsync(int orderId, CancellationToken cancellationToken = default);
    Task<OrderShipment> PurchaseAsync(int orderId, CancellationToken cancellationToken = default);
    Task<OrderShipment> GenerateAsync(int orderId, CancellationToken cancellationToken = default);
    Task<OrderShipment> PrintAsync(int orderId, CancellationToken cancellationToken = default);
    Task<OrderShipment> TrackAsync(int orderId, CancellationToken cancellationToken = default);
}

public interface IMelhorEnvioAccessTokenProvider
{
    Task<string> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class MelhorEnvioAccessTokenProvider : IMelhorEnvioAccessTokenProvider
{
    private readonly HttpClient _http;
    private readonly IMelhorEnvioTokenStore _store;
    private readonly ShippingOptions _options;
    private readonly SemaphoreSlim _lock = new(1, 1);
    public MelhorEnvioAccessTokenProvider(HttpClient http, IMelhorEnvioTokenStore store, IOptions<ShippingOptions> options)
    { _http = http; _store = store; _options = options.Value; }
    public async Task<string> GetAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var stored = await _store.GetAsync(ct);
            if (stored is not null && (stored.ExpiresAtUtc is null || stored.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(1))) return stored.AccessToken;
            var refresh = stored?.RefreshToken ?? _options.RefreshToken;
            if (string.IsNullOrWhiteSpace(refresh))
            {
                if (!string.IsNullOrWhiteSpace(_options.AccessToken)) return _options.AccessToken;
                throw new ShippingOperationException("shipping_token_missing", "A autorização do Melhor Envio não está disponível.", 503);
            }
            if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
                throw new ShippingOperationException("shipping_credentials_missing", "As credenciais OAuth do Melhor Envio não estão configuradas.", 503);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/oauth/token")
            { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["client_id"] = _options.ClientId, ["client_secret"] = _options.ClientSecret, ["refresh_token"] = refresh }) };
            request.Headers.UserAgent.ParseAdd(_options.UserAgent);
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) throw new ShippingOperationException("shipping_token_invalid", "Não foi possível renovar o token do Melhor Envio.", (int)response.StatusCode);
            var token = await response.Content.ReadFromJsonAsync<RefreshTokenResponse>(cancellationToken: ct);
            if (string.IsNullOrWhiteSpace(token?.AccessToken)) throw new ShippingOperationException("shipping_token_invalid", "O Melhor Envio retornou um token inválido.", 502);
            var value = new MelhorEnvioToken(token.AccessToken, string.IsNullOrWhiteSpace(token.RefreshToken) ? refresh : token.RefreshToken!, token.ExpiresIn > 0 ? DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn) : null);
            await _store.SaveAsync(value, ct); return value.AccessToken;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested) { throw new ShippingOperationException("provider_timeout", "A renovação do token expirou.", 504, false, ex); }
        catch (HttpRequestException ex) { throw new ShippingOperationException("provider_unavailable", "O serviço de autorização está indisponível.", 503, false, ex); }
        finally { _lock.Release(); }
    }
    private sealed class RefreshTokenResponse { public string? AccessToken { get; set; } public string? RefreshToken { get; set; } public int ExpiresIn { get; set; } }
}

public sealed class MelhorEnvioShipmentService : IMelhorEnvioShipmentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> Locks = new();
    private readonly HttpClient _http;
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IMelhorEnvioAccessTokenProvider _tokens;
    private readonly ShippingOptions _options;
    private readonly ILogger<MelhorEnvioShipmentService> _logger;

    public MelhorEnvioShipmentService(HttpClient http, IDbContextFactory<AppDbContext> factory,
        IMelhorEnvioAccessTokenProvider tokens, IOptions<ShippingOptions> options,
        ILogger<MelhorEnvioShipmentService> logger)
    { _http = http; _factory = factory; _tokens = tokens; _options = options.Value; _logger = logger; }

    public Task<OrderShipment> PrepareAsync(int orderId, CancellationToken ct = default) => RunLockedAsync(orderId, PrepareCoreAsync, ct);
    public Task<OrderShipment> PurchaseAsync(int orderId, CancellationToken ct = default) => RunLockedAsync(orderId, PurchaseCoreAsync, ct);
    public Task<OrderShipment> GenerateAsync(int orderId, CancellationToken ct = default) => RunLockedAsync(orderId, GenerateCoreAsync, ct);
    public Task<OrderShipment> PrintAsync(int orderId, CancellationToken ct = default) => RunLockedAsync(orderId, PrintCoreAsync, ct);
    public Task<OrderShipment> TrackAsync(int orderId, CancellationToken ct = default) => RunLockedAsync(orderId, TrackCoreAsync, ct);

    private async Task<OrderShipment> RunLockedAsync(int orderId, Func<AppDbContext, OrderShipment, CancellationToken, Task<OrderShipment>> operation, CancellationToken ct)
    {
        var gate = Locks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            var shipment = await LoadAsync(db, orderId, ct);
            try { return await operation(db, shipment, ct); }
            catch (ShippingOperationException ex)
            {
                shipment.LastError = ex.Code;
                shipment.Status = ex.IsIndeterminate ? OrderShipmentStatus.Unknown : OrderShipmentStatus.Failed;
                shipment.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                throw;
            }
        }
        finally { gate.Release(); }
    }

    private static async Task<OrderShipment> LoadAsync(AppDbContext db, int orderId, CancellationToken ct)
    {
        var order = await db.Orders.Include(x => x.Items).ThenInclude(x => x.Product)
            .Include(x => x.PaymentAttempts).Include(x => x.Shipment)
            .SingleOrDefaultAsync(x => x.Id == orderId, ct)
            ?? throw new ShippingOperationException("order_not_found", "Pedido não encontrado.", 404);
        var approved = order.PaymentAttempts.OrderByDescending(x => x.AttemptNumber).FirstOrDefault()?.Status == PaymentStatusWorkflow.Approved;
        if (!approved) throw new ShippingOperationException("payment_not_approved", "O pedido não possui pagamento aprovado.", 409);
        if (order.Status is OrderStatusWorkflow.Cancelled or OrderStatusWorkflow.Shipped or OrderStatusWorkflow.Delivered)
            throw new ShippingOperationException("order_not_eligible", "O status atual não permite esta operação.", 409);
        if (string.Equals(order.ShippingMethod, "RETIRADA", StringComparison.OrdinalIgnoreCase))
            throw new ShippingOperationException("pickup_order", "Pedidos de retirada não possuem etiqueta.", 409);
        if (string.IsNullOrWhiteSpace(order.ZipCode) || string.IsNullOrWhiteSpace(order.Address) ||
            string.IsNullOrWhiteSpace(order.AddressNumber) || string.IsNullOrWhiteSpace(order.Neighborhood) ||
            string.IsNullOrWhiteSpace(order.City) || string.IsNullOrWhiteSpace(order.State))
            throw new ShippingOperationException("incomplete_address", "O endereço estruturado do pedido está incompleto.", 422);
        var shipment = order.Shipment ?? new OrderShipment { OrderId = orderId, Order = order, IdempotencyKey = $"angel-shipment-{orderId}" };
        if (order.Shipment is null) { db.OrderShipments.Add(shipment); await db.SaveChangesAsync(ct); }
        return shipment;
    }

    private async Task<OrderShipment> PrepareCoreAsync(AppDbContext db, OrderShipment shipment, CancellationToken ct)
    {
        if (shipment.Status is OrderShipmentStatus.InCart or OrderShipmentStatus.Purchased or OrderShipmentStatus.Generated or OrderShipmentStatus.Printed)
            return shipment;
        if (shipment.Status == OrderShipmentStatus.Unknown)
            throw new ShippingOperationException("reconciliation_required", "A operação anterior tem resultado indeterminado; reconcilie antes de tentar novamente.", 409);
        var order = shipment.Order;
        var package = _options.StandardPackage;
        ValidateSender(package);
        var body = new
        {
            service = int.TryParse(order.ShippingServiceId, out var serviceId) ? serviceId : 0,
            from = new { name = _options.Sender.Name, email = _options.Sender.Email, phone = _options.Sender.Phone,
                document = _options.Sender.Document, company_document = _options.Sender.CompanyDocument,
                state_register = _options.Sender.StateRegister, economic_activity_code = _options.Sender.EconomicActivityCode,
                address = _options.Sender.Address, complement = _options.Sender.Complement, number = _options.Sender.Number,
                district = _options.Sender.District, city = _options.Sender.City, state_abbr = _options.Sender.State,
                postal_code = _options.Sender.PostalCode },
            to = new { name = order.CustomerName, email = order.Email, phone = order.Phone,
                address = order.Address, complement = order.AddressComplement ?? "", number = order.AddressNumber,
                district = order.Neighborhood, city = order.City, state_abbr = order.State, postal_code = Digits(order.ZipCode) },
            products = order.Items.Select(x => new { name = x.Product.Name, quantity = x.Quantity, unitary_value = x.UnitPrice }).ToArray(),
            volumes = new[] { new { height = package.HeightCm, width = package.WidthCm, length = package.LengthCm,
                weight = package.WeightKg, insurance_value = order.Items.Sum(x => x.UnitPrice * x.Quantity) } },
            options = new { platform = "Angel Acessórios", tags = new[] { new { tag = $"Pedido {order.Id}", url = (string?)null } }, receipt = false, own_hand = false }
        };
        using var response = await SendAsync(HttpMethod.Post, "/api/v2/me/cart", body, ct, indeterminate: true);
        using var json = await ReadJsonAsync(response, ct);
        shipment.ProviderShipmentId = FindString(json.RootElement, "id", "shipment_id", "order_id");
        shipment.ProviderOrderId = FindString(json.RootElement, "order_id", "id");
        shipment.ServiceId = order.ShippingServiceId;
        shipment.ServiceName = order.ShippingServiceName;
        shipment.PurchaseAmount = FindDecimal(json.RootElement, "price", "custom_price");
        if (string.IsNullOrWhiteSpace(shipment.ProviderShipmentId)) throw new ShippingOperationException("invalid_provider_response", "O Melhor Envio não retornou o ID do envio.");
        shipment.Status = OrderShipmentStatus.InCart; shipment.LastError = null; shipment.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return shipment;
    }

    private async Task<OrderShipment> PurchaseCoreAsync(AppDbContext db, OrderShipment shipment, CancellationToken ct)
    {
        if (!_options.PurchaseEnabled) throw new ShippingOperationException("shipping_purchase_disabled", "A compra de etiquetas está desabilitada.", 409);
        if (shipment.Status is OrderShipmentStatus.Purchased or OrderShipmentStatus.Generated or OrderShipmentStatus.Printed) return shipment;
        if (shipment.Status != OrderShipmentStatus.InCart || string.IsNullOrWhiteSpace(shipment.ProviderShipmentId))
            throw new ShippingOperationException("shipment_not_in_cart", "Prepare a remessa antes da compra.", 409);
        using var response = await SendAsync(HttpMethod.Post, "/api/v2/me/shipment/checkout", new { orders = new[] { shipment.ProviderShipmentId } }, ct, indeterminate: true);
        shipment.Status = OrderShipmentStatus.Purchased; shipment.PurchasedAtUtc = DateTime.UtcNow; shipment.LastError = null; shipment.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return shipment;
    }

    private async Task<OrderShipment> GenerateCoreAsync(AppDbContext db, OrderShipment shipment, CancellationToken ct)
    {
        if (shipment.Status is OrderShipmentStatus.Generated or OrderShipmentStatus.Printed) return shipment;
        RequireProviderId(shipment, OrderShipmentStatus.Purchased);
        using var response = await SendAsync(HttpMethod.Post, "/api/v2/me/shipment/generate", new { orders = new[] { shipment.ProviderShipmentId } }, ct, indeterminate: true);
        shipment.Status = OrderShipmentStatus.Generated; shipment.GeneratedAtUtc = DateTime.UtcNow; shipment.LastError = null; shipment.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return shipment;
    }

    private async Task<OrderShipment> PrintCoreAsync(AppDbContext db, OrderShipment shipment, CancellationToken ct)
    {
        if (shipment.Status == OrderShipmentStatus.Printed && !string.IsNullOrWhiteSpace(shipment.LabelUrl)) return shipment;
        RequireProviderId(shipment, OrderShipmentStatus.Generated);
        using var response = await SendAsync(HttpMethod.Post, "/api/v2/me/shipment/print", new { orders = new[] { shipment.ProviderShipmentId }, mode = "private" }, ct);
        using var json = await ReadJsonAsync(response, ct);
        shipment.LabelUrl = FindString(json.RootElement, "url", "link", "pdf");
        shipment.Status = OrderShipmentStatus.Printed; shipment.PrintedAtUtc = DateTime.UtcNow; shipment.LastError = null; shipment.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return shipment;
    }

    private async Task<OrderShipment> TrackCoreAsync(AppDbContext db, OrderShipment shipment, CancellationToken ct)
    {
        RequireProviderId(shipment, null);
        using var response = await SendAsync(HttpMethod.Post, "/api/v2/me/shipment/tracking", new { orders = new[] { shipment.ProviderShipmentId } }, ct);
        using var json = await ReadJsonAsync(response, ct);
        shipment.TrackingCode = FindString(json.RootElement, "tracking", "tracking_code", "tracking_code");
        shipment.TrackingStatus = FindString(json.RootElement, "status", "tracking_status");
        shipment.LastError = null; shipment.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return shipment;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object body, CancellationToken ct, bool indeterminate = false)
    {
        var token = await _tokens.GetAsync(ct);
        using var request = new HttpRequestMessage(method, $"{_options.BaseUrl.TrimEnd('/')}{path}") { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd(_options.UserAgent);
        try
        {
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) { response.Dispose(); throw new ShippingOperationException("provider_error", "O Melhor Envio rejeitou a operação.", (int)response.StatusCode); }
            return response;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested) { throw new ShippingOperationException("provider_timeout", "A operação do Melhor Envio expirou; reconciliação necessária.", 504, indeterminate, ex); }
        catch (HttpRequestException ex) { throw new ShippingOperationException("provider_unavailable", "O Melhor Envio está indisponível.", 503, indeterminate, ex); }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    { try { return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct); } catch (JsonException ex) { throw new ShippingOperationException("invalid_provider_response", "A resposta do Melhor Envio é inválida.", 502, false, ex); } }
    private static void RequireProviderId(OrderShipment shipment, string? requiredStatus)
    { if (string.IsNullOrWhiteSpace(shipment.ProviderShipmentId) || (requiredStatus is not null && shipment.Status != requiredStatus)) throw new ShippingOperationException("shipment_state_invalid", "O estado da remessa não permite esta operação.", 409); }
    private void ValidateSender(StandardShippingPackage package)
    { if (!package.Enabled || package.MaxItems < 1 || string.IsNullOrWhiteSpace(_options.Sender.Name) || string.IsNullOrWhiteSpace(_options.Sender.PostalCode)) throw new ShippingOperationException("shipping_configuration_incomplete", "Embalagem e remetente precisam ser configurados.", 503); }
    private static string Digits(string value) => new(value.Where(char.IsDigit).ToArray());
    private static string? FindString(JsonElement e, params string[] names) { foreach (var n in names) if (e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String) return p.GetString(); return null; }
    private static decimal? FindDecimal(JsonElement e, params string[] names) { foreach (var n in names) if (e.TryGetProperty(n, out var p) && decimal.TryParse(p.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return v; return null; }
}
