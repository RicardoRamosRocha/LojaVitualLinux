using LojaVirtual.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace LojaVirtual.Controllers
{
    [Authorize(Policy = "AdminOnly")]
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
            ViewBag.PendingOrders = _context.Orders.Count(order => order.Status == "Pendente");
            ViewBag.TotalCustomers = _context.Customers.Count();
            ViewBag.RecentOrders = _context.Orders
                .OrderByDescending(order => order.CreatedAt)
                .Take(5)
                .AsNoTracking()
                .ToList();
            ViewBag.LowStockProducts = _context.Products
                .Where(product => product.Stock <= 2)
                .OrderBy(product => product.Stock)
                .ThenBy(product => product.Name)
                .Take(5)
                .AsNoTracking()
                .ToList();

            return View();
        }
    }
}
