using LojaVirtual.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LojaVirtual.ViewModels;

namespace LojaVirtual.Controllers;

public class HomeController : Controller
{
    private readonly AppDbContext _context;

    public HomeController(AppDbContext context)
    {
        _context = context;
    }
   public async Task<IActionResult> Index()
{
    var products = await _context.Products
        .Include(p => p.ProductMedias)
        .ToListAsync();

    var model = new HomeViewModel
    {
        FeaturedProducts = products
    };

    return View(model);
}
    
    public IActionResult Privacy()
    {
        return View();
    }
}