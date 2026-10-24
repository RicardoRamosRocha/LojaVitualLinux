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
    var model = new HomeViewModel
    {
        FeaturedProducts = await _context.Products
            .Include(p => p.ProductMedias)
            .Where(p => p.IsActive && p.IsFeatured)
            .ToListAsync(),

        NewCollectionProducts = await _context.Products
            .Include(p => p.ProductMedias)
            .Where(p => p.IsActive && p.IsNewCollection)
            .ToListAsync(),

        BestSellerProducts = await _context.Products
            .Include(p => p.ProductMedias)
            .Where(p => p.IsActive && p.IsBestSeller)
            .ToListAsync()
    };

    return View(model);
}
    public IActionResult Privacy()
    {
        return View();
    }
}