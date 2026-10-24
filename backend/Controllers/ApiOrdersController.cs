using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LojaVirtual.Data;
using LojaVirtual.Extensions;
using LojaVirtual.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LojaVirtual.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class ApiOrdersController : ControllerBase
{
    private const string CartSessionKey = "api_cart";
    private const string PendingStatus = "Pendente";
    private const string ReservationStatus = "Reservada";
    private const int ReservationLifetimeMinutes = 30;

    private readonly AppDbContext _context;
    private readonly IAntiforgery _antiforgery;

    public ApiOrdersController(AppDbContext context, IAntiforgery antiforgery)
    {
        _context = context;
        _antiforgery = antiforgery;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<ApiOrderResponse>> CreateOrder(
        CreateApiOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
        {
            return BadRequest(ApiOrderError.InvalidIdempotencyKey());
        }

        idempotencyKey = idempotencyKey.Trim();
        var fingerprint = CreateFingerprint(request, idempotencyKey);
        var previousOrder = await _context.Orders
            .Include(order => order.Items)
            .SingleOrDefaultAsync(order => order.IdempotencyKey == idempotencyKey);

        if (previousOrder is not null)
        {
            return previousOrder.IdempotencyFingerprint == fingerprint
                ? Ok(ToResponse(previousOrder))
                : Conflict(ApiOrderError.IdempotencyConflict());
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

        var subtotal = quantities.Sum(item => products[item.Key].Price * item.Value);
        var shippingCost = ShippingCosts[request.ShippingMethod];
        var total = subtotal + shippingCost;

        var order = new Order
        {
            CustomerName = request.CustomerName.Trim(),
            Email = request.Email.Trim(),
            Phone = request.Phone.Trim(),
            Address = request.Address?.Trim() ?? string.Empty,
            ZipCode = request.ZipCode?.Trim() ?? string.Empty,
            ShippingMethod = request.ShippingMethod,
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
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            _context.ChangeTracker.Clear();

            previousOrder = await _context.Orders
                .Include(existing => existing.Items)
                .SingleOrDefaultAsync(existing => existing.IdempotencyKey == idempotencyKey);

            if (previousOrder is not null && previousOrder.IdempotencyFingerprint == fingerprint)
            {
                return Ok(ToResponse(previousOrder));
            }

            return Conflict(ApiOrderError.IdempotencyConflict());
        }

        HttpContext.Session.Remove(CartSessionKey);

        return Created($"/api/orders/{order.Id}", ToResponse(order, subtotal, shippingCost, total));
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
            ShippingMethod = request.ShippingMethod.Trim().ToUpperInvariant()
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
        decimal? total = null)
    {
        var calculatedSubtotal = subtotal ?? order.Items.Sum(item => item.UnitPrice * item.Quantity);
        var calculatedShipping = shippingCost ?? ShippingCosts.GetValueOrDefault(order.ShippingMethod);

        return new ApiOrderResponse
        {
            Id = order.Id,
            Status = order.Status,
            Subtotal = calculatedSubtotal,
            ShippingCost = calculatedShipping,
            Total = total ?? order.Total,
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

    private static ApiOrderError? ValidateCustomerAndShipping(CreateApiOrderRequest request)
    {
        if (!ShippingCosts.ContainsKey(request.ShippingMethod.Trim().ToUpperInvariant()))
        {
            return ApiOrderError.InvalidShippingMethod();
        }

        var requiresDeliveryAddress = !request.ShippingMethod.Equals("RETIRADA", StringComparison.OrdinalIgnoreCase);
        if (requiresDeliveryAddress &&
            (string.IsNullOrWhiteSpace(request.Address) || string.IsNullOrWhiteSpace(request.ZipCode)))
        {
            return ApiOrderError.MissingDeliveryAddress();
        }

        request.ShippingMethod = request.ShippingMethod.Trim().ToUpperInvariant();
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

    [Required]
    public string ShippingMethod { get; set; } = string.Empty;
}

public sealed class ApiOrderResponse
{
    public int Id { get; init; }
    public string Status { get; init; } = string.Empty;
    public decimal Subtotal { get; init; }
    public decimal ShippingCost { get; init; }
    public decimal Total { get; init; }
    public IReadOnlyList<ApiOrderItemResponse> Items { get; init; } = [];
}

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
