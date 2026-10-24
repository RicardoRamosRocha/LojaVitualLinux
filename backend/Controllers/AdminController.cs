using LojaVirtual.Data;
using Microsoft.AspNetCore.Mvc;

namespace LojaVirtual.Controllers
{
    public class AdminController : Controller
    {
        private readonly AppDbContext _context;

        public AdminController(AppDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            ViewBag.TotalProducts = _context.Products.Count();
            ViewBag.TotalCategories = _context.Categories.Count();
            ViewBag.TotalOrders = _context.Orders.Count();
            ViewBag.TotalCustomers = _context.Customers.Count();

            return View();
        }
    }
}