using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LojaVirtual.Data;
using LojaVirtual.Extensions;
using LojaVirtual.Models;
using LojaVirtual.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LojaVirtual.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class ApiOrdersController : ControllerBase
{
    private const string CartSessionKey = "api_cart";
    private const string PendingStatus = "Pendente";
    private const string ReservationStatus = "Reservada";
    private const int ReservationLifetimeMinutes = 30;
    private const string ShippingQuoteSessionKey = "shipping_quote";

    private readonly AppDbContext _context;
    private readonly IAntiforgery _antiforgery;
    private readonly IOrderAccessCredentialService _orderAccess;
    private readonly OrderPaymentService _payments;
    private readonly IOrderRecoveryService _recovery;
    private readonly OrderRecoveryRateLimiter _recoveryRateLimiter;
    private readonly IShippingQuoteService _shippingQuotes;
    private readonly ShippingOptions _shippingOptions;

    public ApiOrdersController(
        AppDbContext context,
        IAntiforgery antiforgery,
        IOrderAccessCredentialService orderAccess,
        OrderPaymentService payments,
        IOrderRecoveryService recovery,
        OrderRecoveryRateLimiter recoveryRateLimiter,
        IShippingQuoteService? shippingQuotes = null,
        IOptions<ShippingOptions>? shippingOptions = null)
    {
        _context = context;
        _antiforgery = antiforgery;
        _orderAccess = orderAccess;
        _payments = payments;
        _recovery = recovery;
        _recoveryRateLimiter = recoveryRateLimiter;
        _shippingQuotes = shippingQuotes ?? new DisabledShippingQuoteService();
        _shippingOptions = shippingOptions?.Value ?? new ShippingOptions();
    }

    [HttpGet("authorized")]
    [EnableRateLimiting("order-tracking")]
    public async Task<ActionResult<IReadOnlyList<ApiOrderResponse>>> GetAuthorizedOrders(
        CancellationToken cancellationToken)
    {
        var access = await _orderAccess.ResolveAsync(Request, cancellationToken);
        if (access.Count == 0)
        {
            return Unauthorized();
        }

        var orderIds = access.Select(item => item.Credential.OrderId).Distinct().ToArray();
        var orders = await _context.Orders
            .Include(order => order.Items)
            .Include(order => order.PaymentAttempts)
            .Where(order => orderIds.Contains(order.Id))
            .ToDictionaryAsync(order => order.Id, cancellationToken);

        var result = access
            .Select(item => orders.TryGetValue(item.Credential.OrderId, out var order)
                ? ToResponse(order)
                : null)
            .Where(order => order is not null)
            .Cast<ApiOrderResponse>()
            .ToArray();

        return Ok(result);
    }

    [HttpPost("authorized/revoke")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("order-tracking")]
    public async Task<IActionResult> RevokeAuthorizedOrders(CancellationToken cancellationToken)
    {
        await _orderAccess.RevokeAllAsync(Request, cancellationToken);
        _orderAccess.DeleteCookie(Response);
        return NoContent();
    }

    [HttpPost("recovery/request")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("order-tracking")]
    public async Task<IActionResult> RequestOrderRecovery(
        OrderRecoveryRequest request,
        CancellationToken cancellationToken)
    {
        var identifier = request.Email.Trim().ToLowerInvariant();
        var clientAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (_recoveryRateLimiter.TryAcquire(clientAddress, identifier))
        {
            await _recovery.RequestAsync(request.Email, cancellationToken);
        }

        return Accepted(new OrderRecoveryResponse());
    }

    [HttpPost("recovery/confirm")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("order-tracking")]
    public async Task<IActionResult> ConfirmOrderRecovery(
        OrderRecoveryConfirmation request,
        CancellationToken cancellationToken)
    {
        var identifier = $"{request.OrderId}:{request.Token}";
        var clientAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!_recoveryRateLimiter.TryAcquire(clientAddress, identifier))
        {
            return Unauthorized(new { code = "recovery_invalid", message = "Não foi possível confirmar o código." });
        }

        var result = await _recovery.ConfirmAsync(
            request.OrderId,
            request.Token,
            Request,
            Response,
            cancellationToken);
        return result.Confirmed
            ? NoContent()
            : Unauthorized(new { code = "recovery_invalid", message = "Não foi possível confirmar o código." });
    }

    [HttpPost("{id:int}/payment")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("order-tracking")]
    public async Task<ActionResult<ApiOrderResponse>> ResumePayment(
        int id,
        CancellationToken cancellationToken)
    {
        var access = await _orderAccess.ResolveAsync(Request, cancellationToken);
        if (!access.Any(item => item.Credential.OrderId == id))
        {
            return Unauthorized();
        }

        var order = await _context.Orders
            .Include(item => item.Items)
            .ThenInclude(item => item.Product)
            .Include(item => item.StockReservations)
            .Include(item => item.PaymentAttempts)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (order is null) return NotFound();

        try
        {
            var paymentAttempt = await _payments.EnsurePreferenceAsync(order, cancellationToken);
            return Ok(ToResponse(order, paymentAttempt: paymentAttempt));
        }
        catch (PaymentEligibilityException exception)
        {
            return Conflict(ApiOrderError.PaymentUnavailable(exception.Code, exception.Message));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("order-create")]
    public async Task<ActionResult<ApiOrderResponse>> CreateOrder(
        CreateApiOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
        {
            return BadRequest(ApiOrderError.InvalidIdempotencyKey());
        }

        idempotencyKey = idempotencyKey.Trim();
        var fingerprint = CreateFingerprint(request, idempotencyKey);
        var previousOrder = await _context.Orders
            .Include(order => order.Items)
            .ThenInclude(item => item.Product)
            .Include(order => order.PaymentAttempts)
            .SingleOrDefaultAsync(order => order.IdempotencyKey == idempotencyKey);

        if (previousOrder is not null)
        {
            if (previousOrder.IdempotencyFingerprint != fingerprint)
            {
                return Conflict(ApiOrderError.IdempotencyConflict());
            }

            var retryAccess = await _orderAccess.PrepareAsync(previousOrder.Id, Request);
            if (retryAccess.NewCredential is not null)
            {
                await _context.SaveChangesAsync();
            }

            _orderAccess.WriteCookie(Response, retryAccess);
            try
            {
                var retryPayment = await _payments.EnsurePreferenceAsync(previousOrder, cancellationToken);
                return Ok(ToResponse(previousOrder, paymentAttempt: retryPayment));
            }
            catch (PaymentEligibilityException exception)
            {
                return Conflict(ApiOrderError.PaymentUnavailable(exception.Code, exception.Message));
            }
        }

        var cart = ReadCart();
        if (cart.Count == 0)
        {
            return BadRequest(ApiOrderError.EmptyCart());
        }

        var validationError = ValidateCustomerAndShipping(request);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        await ReleaseExpiredReservationsAsync();

        var quantities = cart
            .GroupBy(item => item.ProductId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));

        var productIds = quantities.Keys.ToList();
        var products = await _context.Products
            .Where(product => productIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id);

        var reservedQuantities = await _context.StockReservations
            .Where(reservation => reservation.Status == ReservationStatus &&
                                  reservation.ExpiresAt > DateTime.Now &&
                                  productIds.Contains(reservation.ProductId))
            .GroupBy(reservation => reservation.ProductId)
            .Select(group => new { ProductId = group.Key, Quantity = group.Sum(item => item.Quantity) })
            .ToDictionaryAsync(item => item.ProductId, item => item.Quantity);

        var itemErrors = new List<ApiOrderItemError>();
        foreach (var (productId, quantity) in quantities)
        {
            if (!products.TryGetValue(productId, out var product) || !product.IsActive)
            {
                itemErrors.Add(ApiOrderItemError.Unavailable(productId));
                continue;
            }

            var reserved = reservedQuantities.GetValueOrDefault(productId);
            var availableStock = Math.Max(0, product.Stock - reserved);
            if (quantity < 1 || quantity > availableStock)
            {
                itemErrors.Add(ApiOrderItemError.InsufficientStock(productId, availableStock));
            }
        }

        if (itemErrors.Count > 0)
        {
            return Conflict(ApiOrderError.InvalidCart(itemErrors));
        }

        var shippingSelection = ResolveShippingSelection(request, quantities, products);
        if (!shippingSelection.Success)
        {
            return StatusCode(shippingSelection.StatusCode, shippingSelection.Error);
        }

        var subtotal = quantities.Sum(item => products[item.Key].Price * item.Value);
        var shippingCost = shippingSelection.Option!.Price;
        var total = subtotal + shippingCost;

        var order = new Order
        {
            CustomerName = request.CustomerName.Trim(),
            Email = request.Email.Trim(),
            Phone = request.Phone.Trim(),
            Address = request.Address?.Trim() ?? string.Empty,
            ZipCode = request.ZipCode?.Trim() ?? string.Empty,
            ShippingMethod = request.ShippingMethod,
            ShippingCost = shippingCost,
            ShippingProvider = shippingSelection.Option.ProviderName,
            ShippingServiceId = shippingSelection.Option.ServiceId,
            ShippingServiceName = shippingSelection.Option.ServiceName,
            ShippingDeliveryTime = shippingSelection.Option.DeliveryTimeDays > 0
                ? shippingSelection.Option.DeliveryTimeDays
                : null,
            ShippingQuotedAtUtc = shippingSelection.Option.QuotedAtUtc,
            ShippingQuoteId = request.ShippingQuoteId?.Trim(),
            Total = total,
            Status = PendingStatus,
            CreatedAt = DateTime.Now,
            IdempotencyKey = idempotencyKey,
            IdempotencyFingerprint = fingerprint
        };

        foreach (var (productId, quantity) in quantities)
        {
            order.Items.Add(new OrderItem
            {
                ProductId = productId,
                Product = products[productId],
                Quantity = quantity,
                UnitPrice = products[productId].Price
            });

            order.StockReservations.Add(new OrderStockReservation
            {
                ProductId = productId,
                Quantity = quantity,
                Status = ReservationStatus,
                CreatedAt = DateTime.Now,
                ExpiresAt = DateTime.Now.AddMinutes(ReservationLifetimeMinutes)
            });
        }

        _context.Orders.Add(order);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            var orderAccess = await _orderAccess.PrepareAsync(order.Id, Request);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _orderAccess.WriteCookie(Response, orderAccess);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            _context.ChangeTracker.Clear();

            previousOrder = await _context.Orders
                .Include(existing => existing.Items)
                .ThenInclude(item => item.Product)
                .Include(existing => existing.PaymentAttempts)
                .SingleOrDefaultAsync(existing => existing.IdempotencyKey == idempotencyKey);

            if (previousOrder is not null && previousOrder.IdempotencyFingerprint == fingerprint)
            {
                var retryAccess = await _orderAccess.PrepareAsync(previousOrder.Id, Request);
                if (retryAccess.NewCredential is not null)
                {
                    await _context.SaveChangesAsync();
                }

                _orderAccess.WriteCookie(Response, retryAccess);
                try
                {
                    var retryPayment = await _payments.EnsurePreferenceAsync(previousOrder, cancellationToken);
                    return Ok(ToResponse(previousOrder, paymentAttempt: retryPayment));
                }
                catch (PaymentEligibilityException exception)
                {
                    return Conflict(ApiOrderError.PaymentUnavailable(exception.Code, exception.Message));
                }
            }

            return Conflict(ApiOrderError.IdempotencyConflict());
        }

        HttpContext.Session.Remove(CartSessionKey);

        try
        {
            var paymentAttempt = await _payments.EnsurePreferenceAsync(order, cancellationToken);
            return Created($"/api/orders/{order.Id}", ToResponse(order, subtotal, shippingCost, total, paymentAttempt));
        }
        catch (PaymentEligibilityException exception)
        {
            return Conflict(ApiOrderError.PaymentUnavailable(exception.Code, exception.Message));
        }
    }

    [HttpGet("quote")]
    public async Task<ActionResult<ApiOrderQuoteResponse>> Quote(
        [FromQuery] string? shippingMethod,
        [FromQuery] string? zipCode,
        CancellationToken cancellationToken)
    {
        var normalizedShippingMethod = (shippingMethod ?? string.Empty).Trim().ToUpperInvariant();

        var cart = ReadCart();
        if (cart.Count == 0)
        {
            return BadRequest(ApiOrderError.EmptyCart());
        }

        var quantities = cart
            .GroupBy(item => item.ProductId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));
        var products = await _context.Products
            .Where(product => quantities.Keys.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        if (quantities.Keys.Any(productId => !products.TryGetValue(productId, out var product) || !product.IsActive))
        {
            return Conflict(ApiOrderError.InvalidCart([]));
        }

        var subtotal = quantities.Sum(item => products[item.Key].Price * item.Value);

        if (normalizedShippingMethod == "RETIRADA")
        {
            return Ok(ApiOrderQuoteResponse.Pickup(subtotal));
        }

        if (!_shippingOptions.RealQuotesEnabled)
        {
            var provisionalOptions = new List<ApiShippingOption>();
            if (string.IsNullOrWhiteSpace(normalizedShippingMethod) || normalizedShippingMethod is "ALL" or "PAC")
            {
                provisionalOptions.Add(new ApiShippingOption("PAC", "PAC", "Provisório", 15m, null));
            }
            if (string.IsNullOrWhiteSpace(normalizedShippingMethod) || normalizedShippingMethod is "ALL" or "SEDEX")
            {
                provisionalOptions.Add(new ApiShippingOption("SEDEX", "SEDEX", "Provisório", 30m, null));
            }

            if (provisionalOptions.Count == 0)
            {
                return BadRequest(ApiOrderError.InvalidShippingMethod());
            }

            return Ok(ApiOrderQuoteResponse.Provisional(subtotal, provisionalOptions));
        }

        if (!IsValidPostalCode(zipCode))
        {
            return BadRequest(ApiOrderError.InvalidZipCode());
        }

        try
        {
            var quote = await _shippingQuotes.QuoteAsync(
                NormalizePostalCode(zipCode!),
                quantities.Select(item => new ShippingQuoteItem(item.Key, item.Value, products[item.Key].Price)).ToList(),
                cancellationToken);
            var quoteId = Guid.NewGuid().ToString("N");
            var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(_shippingOptions.QuoteTtlMinutes, 1, 60));
            var options = quote.Options
                .Select(option => new ApiShippingOption(
                    option.ServiceId,
                    option.ServiceName,
                    option.ProviderName,
                    option.Price,
                    option.DeliveryTimeDays))
                .ToList();
            HttpContext.Session.SetObjectAsJson(ShippingQuoteSessionKey, new ShippingQuoteSnapshot
            {
                QuoteId = quoteId,
                DestinationPostalCode = NormalizePostalCode(zipCode!),
                CartFingerprint = CreateCartFingerprint(quantities, products),
                ExpiresAtUtc = expiresAtUtc,
                Options = options
            });

            return Ok(ApiOrderQuoteResponse.Real(
                subtotal,
                quoteId,
                expiresAtUtc,
                options));
        }
        catch (ShippingQuoteException exception)
        {
            return StatusCode(exception.StatusCode, ApiOrderError.FromShippingException(exception));
        }
    }

    private async Task ReleaseExpiredReservationsAsync()
    {
        var expired = await _context.StockReservations
            .Where(reservation => reservation.Status == ReservationStatus && reservation.ExpiresAt <= DateTime.Now)
            .ToListAsync();

        foreach (var reservation in expired)
        {
            reservation.Status = "Liberada";
            reservation.ReleasedAt = DateTime.Now;
        }

        if (expired.Count > 0)
        {
            await _context.SaveChangesAsync();
        }
    }

    private string CreateFingerprint(CreateApiOrderRequest request, string idempotencyKey)
    {
        var payload = JsonSerializer.Serialize(new
        {
            // The idempotency key is client supplied, so bind retries to the
            // session that originally authorized the checkout context.
            Session = Hash(HttpContext.Session.Id),
            IdempotencyKey = idempotencyKey,
            CustomerName = request.CustomerName.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            Phone = request.Phone.Trim(),
            ZipCode = request.ZipCode?.Trim() ?? string.Empty,
            Address = request.Address?.Trim() ?? string.Empty,
            AddressNumber = request.AddressNumber?.Trim() ?? string.Empty,
            AddressComplement = request.AddressComplement?.Trim() ?? string.Empty,
            Neighborhood = request.Neighborhood?.Trim() ?? string.Empty,
            City = request.City?.Trim() ?? string.Empty,
            State = request.State?.Trim().ToUpperInvariant() ?? string.Empty,
            ShippingMethod = request.ShippingMethod.Trim().ToUpperInvariant(),
            ShippingQuoteId = request.ShippingQuoteId?.Trim() ?? string.Empty,
            ShippingServiceId = request.ShippingServiceId?.Trim() ?? string.Empty
        });

        return Hash(payload);
    }

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static ApiOrderResponse ToResponse(
        Order order,
        decimal? subtotal = null,
        decimal? shippingCost = null,
        decimal? total = null,
        OrderPaymentAttempt? paymentAttempt = null)
    {
        var calculatedSubtotal = subtotal ?? order.Items.Sum(item => item.UnitPrice * item.Quantity);
        var calculatedShipping = shippingCost ?? order.ShippingCost ?? ShippingCosts.GetValueOrDefault(order.ShippingMethod);

        return new ApiOrderResponse
        {
            Id = order.Id,
            Status = order.Status,
            CreatedAt = order.CreatedAt,
            ShippingMethod = order.ShippingMethod,
            ShippingProvider = order.ShippingProvider,
            ShippingServiceId = order.ShippingServiceId,
            ShippingServiceName = order.ShippingServiceName,
            ShippingDeliveryTime = order.ShippingDeliveryTime,
            Subtotal = calculatedSubtotal,
            ShippingCost = calculatedShipping,
            Total = total ?? order.Total,
            PaymentStatus = paymentAttempt?.Status ?? order.PaymentAttempts
                .OrderByDescending(item => item.AttemptNumber)
                .Select(item => item.Status)
                .FirstOrDefault(),
            CheckoutUrl = paymentAttempt?.CheckoutUrl ?? order.PaymentAttempts
                .OrderByDescending(item => item.AttemptNumber)
                .Select(item => item.CheckoutUrl)
                .FirstOrDefault(),
            Items = order.Items.Select(item => new ApiOrderItemResponse
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                Subtotal = item.UnitPrice * item.Quantity
            }).ToList()
        };
    }

    private static readonly IReadOnlyDictionary<string, decimal> ShippingCosts =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["PAC"] = 15m,
            ["SEDEX"] = 30m,
            ["RETIRADA"] = 0m
        };

    private ShippingSelectionResult ResolveShippingSelection(
        CreateApiOrderRequest request,
        IReadOnlyDictionary<int, int> quantities,
        IReadOnlyDictionary<int, Product> products)
    {
        if (request.ShippingMethod.Equals("RETIRADA", StringComparison.OrdinalIgnoreCase))
        {
            return ShippingSelectionResult.Ok(new ShippingSelection(
                "RETIRADA",
                "Retirada",
                "Angel Acessórios",
                0m,
                0,
                null));
        }

        if (!_shippingOptions.RealQuotesEnabled)
        {
            return ShippingSelectionResult.Fail(ApiOrderError.RealShippingDisabled(), 409);
        }

        var snapshot = HttpContext.Session.GetObjectFromJson<ShippingQuoteSnapshot>(ShippingQuoteSessionKey);
        if (snapshot is null || !string.Equals(snapshot.QuoteId, request.ShippingQuoteId, StringComparison.Ordinal))
        {
            return ShippingSelectionResult.Fail(ApiOrderError.QuoteExpired(), 409);
        }

        if (snapshot.ExpiresAtUtc <= DateTimeOffset.UtcNow ||
            !string.Equals(snapshot.DestinationPostalCode, NormalizePostalCode(request.ZipCode!), StringComparison.Ordinal) ||
            !string.Equals(snapshot.CartFingerprint, CreateCartFingerprint(quantities, products), StringComparison.Ordinal))
        {
            return ShippingSelectionResult.Fail(ApiOrderError.QuoteExpired(), 409);
        }

        var option = snapshot.Options.FirstOrDefault(item =>
            string.Equals(item.ServiceId, request.ShippingServiceId, StringComparison.Ordinal));
        if (option is null)
        {
            return ShippingSelectionResult.Fail(ApiOrderError.InvalidQuote(), 409);
        }

        return ShippingSelectionResult.Ok(new ShippingSelection(
            option.ServiceId,
            option.ServiceName,
            option.ProviderName,
            option.Price,
            option.DeliveryTimeDays ?? 0,
            snapshot.ExpiresAtUtc));
    }

    private string CreateCartFingerprint(
        IReadOnlyDictionary<int, int> quantities,
        IReadOnlyDictionary<int, Product> products)
    {
        var payload = JsonSerializer.Serialize(quantities
            .OrderBy(item => item.Key)
            .Select(item => new
            {
                ProductId = item.Key,
                Quantity = item.Value,
                UnitPrice = products[item.Key].Price
            }));
        return Hash(payload);
    }

    private static bool IsValidPostalCode(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        System.Text.RegularExpressions.Regex.IsMatch(value.Trim(), @"^\d{5}-?\d{3}$");

    private static string NormalizePostalCode(string value) =>
        new string(value.Where(char.IsDigit).ToArray());

    private static ApiOrderError? ValidateCustomerAndShipping(CreateApiOrderRequest request)
    {
        request.ShippingMethod = request.ShippingMethod.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(request.ShippingMethod))
        {
            return ApiOrderError.InvalidShippingMethod();
        }

        var requiresDeliveryAddress = !request.ShippingMethod.Equals("RETIRADA", StringComparison.OrdinalIgnoreCase);
        if (requiresDeliveryAddress &&
            (string.IsNullOrWhiteSpace(request.Address) || string.IsNullOrWhiteSpace(request.ZipCode)))
        {
            return ApiOrderError.MissingDeliveryAddress();
        }

        if (requiresDeliveryAddress &&
            (string.IsNullOrWhiteSpace(request.AddressNumber) ||
             string.IsNullOrWhiteSpace(request.Neighborhood) ||
             string.IsNullOrWhiteSpace(request.City) ||
             string.IsNullOrWhiteSpace(request.State)))
        {
            return ApiOrderError.MissingDeliveryAddress();
        }

        if (requiresDeliveryAddress && !IsValidPostalCode(request.ZipCode))
        {
            return ApiOrderError.InvalidZipCode();
        }

        if (requiresDeliveryAddress && string.IsNullOrWhiteSpace(request.ShippingServiceId))
        {
            return ApiOrderError.InvalidShippingMethod();
        }

        return null;
    }

    private List<ApiCartItemSession> ReadCart()
    {
        return HttpContext.Session.GetObjectFromJson<List<ApiCartItemSession>>(CartSessionKey) ?? [];
    }
}

public sealed class CreateApiOrderRequest
{
    [Required, StringLength(120, MinimumLength = 2)]
    public string CustomerName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(180)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(30, MinimumLength = 8)]
    public string Phone { get; set; } = string.Empty;

    [StringLength(20)]
    public string? ZipCode { get; set; }

    [StringLength(240)]
    public string? Address { get; set; }

    [StringLength(20)]
    public string? AddressNumber { get; set; }

    [StringLength(80)]
    public string? AddressComplement { get; set; }

    [StringLength(80)]
    public string? Neighborhood { get; set; }

    [StringLength(80)]
    public string? City { get; set; }

    [StringLength(2)]
    public string? State { get; set; }

    [Required]
    public string ShippingMethod { get; set; } = string.Empty;

    [StringLength(80)]
    public string? ShippingQuoteId { get; set; }

    [StringLength(80)]
    public string? ShippingServiceId { get; set; }
}

public sealed class OrderRecoveryRequest
{
    [Required, EmailAddress, StringLength(180)]
    public string Email { get; set; } = string.Empty;
}

public sealed class OrderRecoveryConfirmation
{
    [Range(1, int.MaxValue)]
    public int OrderId { get; set; }

    [Required, StringLength(128, MinimumLength = 20)]
    public string Token { get; set; } = string.Empty;
}

public sealed class OrderRecoveryResponse
{
    public string Message { get; init; } = "Se os dados corresponderem, enviaremos um código de recuperação.";
}

public sealed class ApiOrderResponse
{
    public int Id { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public string ShippingMethod { get; init; } = string.Empty;
    public string? ShippingProvider { get; init; }
    public string? ShippingServiceId { get; init; }
    public string? ShippingServiceName { get; init; }
    public int? ShippingDeliveryTime { get; init; }
    public decimal Subtotal { get; init; }
    public decimal ShippingCost { get; init; }
    public decimal Total { get; init; }
    public string? PaymentStatus { get; init; }
    public string? CheckoutUrl { get; init; }
    public IReadOnlyList<ApiOrderItemResponse> Items { get; init; } = [];
}

public sealed class ApiOrderQuoteResponse
{
    public string ShippingMethod { get; init; } = string.Empty;
    public bool IsRealQuote { get; init; }
    public bool CanCreateOrder { get; init; }
    public string QuoteStatus { get; init; } = string.Empty;
    public string? QuoteId { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public decimal Subtotal { get; init; }
    public decimal ShippingCost { get; init; }
    public decimal Total { get; init; }
    public IReadOnlyList<ApiShippingOption> Options { get; init; } = [];

    public static ApiOrderQuoteResponse Pickup(decimal subtotal) => new()
    {
        ShippingMethod = "RETIRADA",
        IsRealQuote = false,
        CanCreateOrder = true,
        QuoteStatus = "pickup",
        Subtotal = subtotal,
        ShippingCost = 0m,
        Total = subtotal,
        Options = [new ApiShippingOption("RETIRADA", "Retirada", "Angel Acessórios", 0m, null)]
    };

    public static ApiOrderQuoteResponse Provisional(decimal subtotal, IReadOnlyList<ApiShippingOption> options) => new()
    {
        ShippingMethod = "PROVISIONAL",
        IsRealQuote = false,
        CanCreateOrder = false,
        QuoteStatus = "real_quotes_disabled",
        Subtotal = subtotal,
        Options = options
    };

    public static ApiOrderQuoteResponse Real(
        decimal subtotal,
        string quoteId,
        DateTimeOffset expiresAtUtc,
        IReadOnlyList<ApiShippingOption> options) => new()
    {
        ShippingMethod = "MELHOR_ENVIO",
        IsRealQuote = true,
        CanCreateOrder = true,
        QuoteStatus = "ready",
        QuoteId = quoteId,
        ExpiresAtUtc = expiresAtUtc,
        Subtotal = subtotal,
        Options = options
    };
}

public sealed record ApiShippingOption(
    string ServiceId,
    string ServiceName,
    string ProviderName,
    decimal Price,
    int? DeliveryTimeDays);

public sealed class ApiOrderItemResponse
{
    public int ProductId { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal Subtotal { get; init; }
}

public sealed class ApiOrderError
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<ApiOrderItemError> Items { get; init; } = [];

    public static ApiOrderError EmptyCart() => new()
    {
        Code = "empty_cart",
        Message = "O carrinho está vazio."
    };

    public static ApiOrderError InvalidCart(IReadOnlyList<ApiOrderItemError> items) => new()
    {
        Code = "invalid_cart",
        Message = "Um ou mais produtos não estão mais disponíveis nas condições atuais.",
        Items = items
    };

    public static ApiOrderError InvalidShippingMethod() => new()
    {
        Code = "invalid_shipping_method",
        Message = "Método de envio inválido. Use PAC, SEDEX ou RETIRADA."
    };

    public static ApiOrderError MissingDeliveryAddress() => new()
    {
        Code = "missing_delivery_address",
        Message = "Endereço e CEP são obrigatórios para entrega."
    };

    public static ApiOrderError InvalidZipCode() => new()
    {
        Code = "invalid_zip_code",
        Message = "Informe um CEP brasileiro válido com 8 dígitos."
    };

    public static ApiOrderError RealShippingDisabled() => new()
    {
        Code = "real_shipping_disabled",
        Message = "O frete real ainda não está habilitado para pedidos de entrega."
    };

    public static ApiOrderError QuoteExpired() => new()
    {
        Code = "shipping_quote_expired",
        Message = "A cotação de frete expirou ou não corresponde mais ao carrinho. Calcule novamente."
    };

    public static ApiOrderError InvalidQuote() => new()
    {
        Code = "shipping_quote_invalid",
        Message = "A opção de frete selecionada não é válida para esta cotação."
    };

    public static ApiOrderError FromShippingException(ShippingQuoteException exception) => new()
    {
        Code = exception.Code,
        Message = exception.Message
    };

    public static ApiOrderError InvalidIdempotencyKey() => new()
    {
        Code = "invalid_idempotency_key",
        Message = "Informe uma chave de idempotência válida no header Idempotency-Key."
    };

    public static ApiOrderError IdempotencyConflict() => new()
    {
        Code = "idempotency_conflict",
        Message = "A chave de idempotência já foi usada com dados diferentes."
    };

    public static ApiOrderError PaymentUnavailable(string code, string message) => new()
    {
        Code = code,
        Message = message
    };
}

public sealed class ShippingQuoteSnapshot
{
    public string QuoteId { get; set; } = string.Empty;
    public string DestinationPostalCode { get; set; } = string.Empty;
    public string CartFingerprint { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public List<ApiShippingOption> Options { get; set; } = [];
}

public sealed record ShippingSelection(
    string ServiceId,
    string ServiceName,
    string ProviderName,
    decimal Price,
    int DeliveryTimeDays,
    DateTimeOffset? QuotedAtUtc);

public sealed record ShippingSelectionResult(
    bool Success,
    ShippingSelection? Option,
    ApiOrderError? Error,
    int StatusCode)
{
    public static ShippingSelectionResult Ok(ShippingSelection option) => new(true, option, null, 200);
    public static ShippingSelectionResult Fail(ApiOrderError error, int statusCode) => new(false, null, error, statusCode);
}

public sealed class ApiOrderItemError
{
    public int ProductId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public int? AvailableStock { get; init; }

    public static ApiOrderItemError Unavailable(int productId) => new()
    {
        ProductId = productId,
        Code = "product_not_available",
        Message = "O produto não está mais disponível."
    };

    public static ApiOrderItemError InsufficientStock(int productId, int availableStock) => new()
    {
        ProductId = productId,
        Code = "insufficient_stock",
        Message = "O estoque disponível é inferior à quantidade do carrinho.",
        AvailableStock = availableStock
    };
}
