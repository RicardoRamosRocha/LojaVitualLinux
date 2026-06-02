using LojaVirtual.Data;
using LojaVirtual.Extensions;
using LojaVirtual.Models;
using LojaVirtual.ViewModels;
using LojaVirtual.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace LojaVirtual.Controllers
{
    public class CheckoutController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IMercadoPagoService _mercadoPagoService;

        public CheckoutController(
            AppDbContext context,
            IMercadoPagoService mercadoPagoService)
        {
            _context = context;
            _mercadoPagoService = mercadoPagoService;
        }

        [HttpGet]
        public IActionResult Index()
        {
            var cart = HttpContext.Session
                .GetObjectFromJson<List<CartItem>>("cart")
                ?? new List<CartItem>();

            ViewBag.Cart = cart;

            var model = new CheckoutViewModel
            {
                Total = cart.Sum(x => x.Price * x.Quantity)
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(CheckoutViewModel model)
        {
            var cart = HttpContext.Session
                .GetObjectFromJson<List<CartItem>>("cart")
                ?? new List<CartItem>();

            if (!cart.Any())
            {
                ModelState.AddModelError("", "Carrinho vazio.");
                ViewBag.Cart = cart;
                return View(model);
            }

            model.Total = cart.Sum(x => x.Price * x.Quantity);

            if (!ModelState.IsValid)
            {
                ViewBag.Cart = cart;
                return View(model);
            }

            var order = new Order
            {
                CustomerName = model.CustomerName,
                Email = model.Email,
                Phone = model.Phone,
                Address = model.Address,
                Total = model.Total,
                Status = "Pendente",
                CreatedAt = DateTime.Now
            };

            _context.Orders.Add(order);

            await _context.SaveChangesAsync();

            foreach (var item in cart)
            {
                var orderItem = new OrderItem
                {
                    OrderId = order.Id,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = item.Price
                };

                _context.OrderItems.Add(orderItem);
            }

            await _context.SaveChangesAsync();

            var pagamentoUrl =
                await _mercadoPagoService
                    .CriarPreferenciaAsync(order);

            return Redirect(pagamentoUrl);
        }

        [HttpGet]
        public IActionResult Success(int id)
        {
            ViewBag.OrderId = id;

            return View();
        }

        [HttpGet]
        public IActionResult Failure()
        {
            return View();
        }

        [HttpGet]
        public IActionResult Pending()
        {
            return View();
        }

    }

}

