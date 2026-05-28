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
        var viewModel = new HomeViewModel
        {
            FeaturedProducts = await _context.Products
                .OrderByDescending(p => p.Id)
                .Take(6)
                .ToListAsync()
        };

        return View(viewModel);
    }
    public IActionResult Privacy()
    {
        return View();
    }
}