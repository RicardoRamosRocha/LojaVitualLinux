using LojaVirtual.Data;
using LojaVirtual.Extensions;
using LojaVirtual.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LojaVirtual.Controllers
{
    public class CartController : Controller
    {
        private readonly AppDbContext _context;

        public CartController(AppDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            var cart = HttpContext.Session
                .GetObjectFromJson<List<CartItem>>("cart")
                ?? new List<CartItem>();

            return View(cart);
        }

        public async Task<IActionResult> Add(int id)
        {
            var product = await _context.Products
                .Include(p => p.ProductMedias)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
                return NotFound();

            var cart = HttpContext.Session
                .GetObjectFromJson<List<CartItem>>("cart")
                ?? new List<CartItem>();

            var item = cart.FirstOrDefault(x => x.ProductId == id);

            if (item != null)
            {
                item.Quantity++;
            }
            else
            {
                cart.Add(new CartItem
                {
                    ProductId = product.Id,
                    Name = product.Name,
                    Price = product.Price,
                    Quantity = 1,
                    ImageUrl = product.ProductMedias
                        .FirstOrDefault(x => x.Type == "image")?.Url
                });
            }

            HttpContext.Session.SetObjectAsJson("cart", cart);

            return RedirectToAction("Index");
        }

        public IActionResult Remove(int id)
        {
            var cart = HttpContext.Session
                .GetObjectFromJson<List<CartItem>>("cart")
                ?? new List<CartItem>();

            var item = cart.FirstOrDefault(x => x.ProductId == id);

            if (item != null)
            {
                cart.Remove(item);
            }

            HttpContext.Session.SetObjectAsJson("cart", cart);

            return RedirectToAction("Index");
        }
    }
}