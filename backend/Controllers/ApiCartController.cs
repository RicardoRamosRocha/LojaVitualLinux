using System.ComponentModel.DataAnnotations;
using LojaVirtual.Data;
using LojaVirtual.Extensions;
using LojaVirtual.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LojaVirtual.Controllers;

[ApiController]
[Route("api/cart")]
public sealed class ApiCartController : ControllerBase
{
    private const string SessionKey = "api_cart";
    private const int MaxQuantity = 1000;

    private readonly AppDbContext _context;
    private readonly IAntiforgery _antiforgery;

    public ApiCartController(AppDbContext context, IAntiforgery antiforgery)
    {
        _context = context;
        _antiforgery = antiforgery;
    }

    [HttpGet]
    public async Task<ActionResult<ApiCartResponse>> GetCart()
    {
        IssueAntiforgeryToken();

        var (response, changed) = await BuildCartResponseAsync();
        if (changed)
        {
            SaveCart(response.Items
                .Select(item => new ApiCartItemSession
                {
                    ProductId = item.ProductId,
                    Quantity = item.Quantity
                })
                .ToList());
        }

        return Ok(response);
    }

    [HttpPost("items")]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<ApiCartResponse>> AddItem(AddCartItemRequest request)
    {
        if (request.Quantity is < 1 or > MaxQuantity)
        {
            return BadRequest(CartErrorResponse.InvalidQuantity(request.ProductId));
        }

        var product = await GetAvailableProductAsync(request.ProductId);
        if (product is null)
        {
            return NotFound(CartErrorResponse.ProductNotAvailable(request.ProductId));
        }

        var cart = ReadCart();
        var item = cart.FirstOrDefault(x => x.ProductId == request.ProductId);
        var resultingQuantity = (item?.Quantity ?? 0) + request.Quantity;

        if (resultingQuantity > product.Stock)
        {
            return Conflict(CartErrorResponse.InsufficientStock(
                request.ProductId,
                product.Stock));
        }

        if (item is null)
        {
            cart.Add(new ApiCartItemSession
            {
                ProductId = request.ProductId,
                Quantity = request.Quantity
            });
        }
        else
        {
            item.Quantity = resultingQuantity;
        }

        SaveCart(cart);
        return Ok((await BuildCartResponseAsync()).Response);
    }

    [HttpPatch("items/{productId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<ApiCartResponse>> UpdateItem(
        int productId,
        UpdateCartItemRequest request)
    {
        if (request.Quantity is < 1 or > MaxQuantity)
        {
            return BadRequest(CartErrorResponse.InvalidQuantity(productId));
        }

        var product = await GetAvailableProductAsync(productId);
        if (product is null)
        {
            return NotFound(CartErrorResponse.ProductNotAvailable(productId));
        }

        if (request.Quantity > product.Stock)
        {
            return Conflict(CartErrorResponse.InsufficientStock(
                productId,
                product.Stock));
        }

        var cart = ReadCart();
        var item = cart.FirstOrDefault(x => x.ProductId == productId);
        if (item is null)
        {
            return NotFound(CartErrorResponse.ItemNotInCart(productId));
        }

        item.Quantity = request.Quantity;
        SaveCart(cart);

        return Ok((await BuildCartResponseAsync()).Response);
    }

    [HttpDelete("items/{productId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<ApiCartResponse>> RemoveItem(int productId)
    {
        var cart = ReadCart();
        cart.RemoveAll(item => item.ProductId == productId);
        SaveCart(cart);

        return Ok((await BuildCartResponseAsync()).Response);
    }

    [HttpDelete]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<ApiCartResponse>> ClearCart()
    {
        HttpContext.Session.Remove(SessionKey);
        return Ok((await BuildCartResponseAsync()).Response);
    }

    private async Task<(ApiCartResponse Response, bool Changed)> BuildCartResponseAsync()
    {
        var sessionItems = ReadCart();
        if (sessionItems.Count == 0)
        {
            return (ApiCartResponse.Empty(), false);
        }

        var productIds = sessionItems
            .Select(item => item.ProductId)
            .Distinct()
            .ToList();

        var products = await _context.Products
            .AsNoTracking()
            .Where(product => productIds.Contains(product.Id))
            .Select(product => new ProductSnapshot
            {
                Id = product.Id,
                Name = product.Name,
                Price = product.Price,
                Stock = product.Stock,
                IsActive = product.IsActive,
                ImageUrl = product.ProductMedias
                    .Where(media => media.Type == "image")
                    .Select(media => media.Url)
                    .FirstOrDefault()
            })
            .ToDictionaryAsync(product => product.Id);

        var reservedQuantities = await _context.StockReservations
            .Where(reservation => reservation.Status == "Reservada" &&
                                  reservation.ExpiresAt > DateTime.Now &&
                                  productIds.Contains(reservation.ProductId))
            .GroupBy(reservation => reservation.ProductId)
            .Select(group => new { ProductId = group.Key, Quantity = group.Sum(item => item.Quantity) })
            .ToDictionaryAsync(item => item.ProductId, item => item.Quantity);

        foreach (var product in products.Values)
        {
            product.Stock = Math.Max(0, product.Stock - reservedQuantities.GetValueOrDefault(product.Id));
        }

        var items = new List<ApiCartItemResponse>();
        var changed = false;

        foreach (var sessionItem in sessionItems)
        {
            if (!products.TryGetValue(sessionItem.ProductId, out var product) ||
                !product.IsActive ||
                product.Stock <= 0)
            {
                changed = true;
                continue;
            }

            var quantity = Math.Min(sessionItem.Quantity, product.Stock);
            if (quantity < 1)
            {
                changed = true;
                continue;
            }

            if (quantity != sessionItem.Quantity)
            {
                changed = true;
            }

            items.Add(new ApiCartItemResponse
            {
                ProductId = product.Id,
                Name = product.Name,
                Quantity = quantity,
                UnitPrice = product.Price,
                Subtotal = product.Price * quantity,
                ImageUrl = product.ImageUrl,
                AvailableStock = product.Stock
            });
        }

        return (new ApiCartResponse
        {
            Items = items,
            Subtotal = items.Sum(item => item.Subtotal),
            TotalQuantity = items.Sum(item => item.Quantity)
        }, changed);
    }

    private async Task<ProductSnapshot?> GetAvailableProductAsync(int productId)
    {
        var product = await _context.Products
            .AsNoTracking()
            .Where(product => product.Id == productId && product.IsActive)
            .Select(product => new ProductSnapshot
            {
                Id = product.Id,
                Name = product.Name,
                Price = product.Price,
                Stock = product.Stock,
                IsActive = product.IsActive,
                ImageUrl = product.ProductMedias
                    .Where(media => media.Type == "image")
                    .Select(media => media.Url)
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync();

        if (product is null)
        {
            return null;
        }

        var reservedQuantity = await _context.StockReservations
            .Where(reservation => reservation.ProductId == productId &&
                                  reservation.Status == "Reservada" &&
                                  reservation.ExpiresAt > DateTime.Now)
            .SumAsync(reservation => (int?)reservation.Quantity) ?? 0;

        product.Stock = Math.Max(0, product.Stock - reservedQuantity);
        return product;
    }

    private List<ApiCartItemSession> ReadCart()
    {
        return HttpContext.Session
                   .GetObjectFromJson<List<ApiCartItemSession>>(SessionKey)
               ?? [];
    }

    private void SaveCart(List<ApiCartItemSession> cart)
    {
        if (cart.Count == 0)
        {
            HttpContext.Session.Remove(SessionKey);
            return;
        }

        HttpContext.Session.SetObjectAsJson(SessionKey, cart);
    }

    private void IssueAntiforgeryToken()
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        if (!string.IsNullOrWhiteSpace(tokens.RequestToken))
        {
            Response.Headers["X-CSRF-TOKEN"] = tokens.RequestToken;
        }
    }

    private sealed class ProductSnapshot
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public bool IsActive { get; set; }
        public string? ImageUrl { get; set; }
    }
}

public sealed class AddCartItemRequest
{
    public int ProductId { get; set; }

    [Range(1, 1000)]
    public int Quantity { get; set; }
}

public sealed class UpdateCartItemRequest
{
    [Range(1, 1000)]
    public int Quantity { get; set; }
}

public sealed class ApiCartResponse
{
    public IReadOnlyList<ApiCartItemResponse> Items { get; set; } = [];
    public decimal Subtotal { get; set; }
    public int TotalQuantity { get; set; }

    public static ApiCartResponse Empty() => new();
}

public sealed class ApiCartItemResponse
{
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal { get; set; }
    public string? ImageUrl { get; set; }
    public int AvailableStock { get; set; }
}

public sealed class CartErrorResponse
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public int? ProductId { get; init; }
    public int? AvailableStock { get; init; }

    public static CartErrorResponse InvalidQuantity(int productId) => new()
    {
        Code = "invalid_quantity",
        Message = "A quantidade deve ser um número inteiro entre 1 e 1000.",
        ProductId = productId
    };

    public static CartErrorResponse ProductNotAvailable(int productId) => new()
    {
        Code = "product_not_available",
        Message = "O produto não está disponível.",
        ProductId = productId
    };

    public static CartErrorResponse InsufficientStock(int productId, int availableStock) => new()
    {
        Code = "insufficient_stock",
        Message = "A quantidade solicitada excede o estoque disponível.",
        ProductId = productId,
        AvailableStock = availableStock
    };

    public static CartErrorResponse ItemNotInCart(int productId) => new()
    {
        Code = "item_not_in_cart",
        Message = "O produto não está no carrinho.",
        ProductId = productId
    };
}
