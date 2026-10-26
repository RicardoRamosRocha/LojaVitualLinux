using LojaVirtual.Data;
using LojaVirtual.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LojaVirtual.Controllers;

[Authorize(Policy = "AdminOnly")]
public class CategoriesController : Controller
{
    private readonly AppDbContext _context;

    public CategoriesController(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var categories = await _context.Categories
            .AsNoTracking()
            .OrderBy(category => category.Name)
            .Select(category => new CategoryListItem
            {
                Id = category.Id,
                Name = category.Name,
                ProductCount = category.Products.Count
            })
            .ToListAsync();

        return View(categories);
    }

    public IActionResult Create() => View(new Category());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Name")] Category category)
    {
        category.Name = category.Name?.Trim() ?? string.Empty;
        await ValidateUniqueNameAsync(category.Name);

        if (!ModelState.IsValid)
        {
            return View(category);
        }

        _context.Categories.Add(category);
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = "Categoria cadastrada com sucesso.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var category = await _context.Categories.FindAsync(id.Value);
        return category is null ? NotFound() : View(category);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,Name")] Category category)
    {
        if (id != category.Id)
        {
            return NotFound();
        }

        category.Name = category.Name?.Trim() ?? string.Empty;
        await ValidateUniqueNameAsync(category.Name, category.Id);

        if (!ModelState.IsValid)
        {
            return View(category);
        }

        var existing = await _context.Categories.FindAsync(id);
        if (existing is null)
        {
            return NotFound();
        }

        existing.Name = category.Name;
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = "Categoria atualizada com sucesso.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var category = await _context.Categories
            .AsNoTracking()
            .Where(item => item.Id == id.Value)
            .Select(item => new CategoryDeleteViewModel
            {
                Id = item.Id,
                Name = item.Name,
                ProductCount = item.Products.Count
            })
            .SingleOrDefaultAsync();

        return category is null ? NotFound() : View(category);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var category = await _context.Categories
            .Include(item => item.Products)
            .SingleOrDefaultAsync(item => item.Id == id);

        if (category is null)
        {
            return NotFound();
        }

        if (category.Products.Count != 0)
        {
            TempData["ErrorMessage"] = "Não é possível excluir uma categoria associada a produtos.";
            return RedirectToAction(nameof(Index));
        }

        _context.Categories.Remove(category);
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = "Categoria excluída com sucesso.";
        return RedirectToAction(nameof(Index));
    }

    private async Task ValidateUniqueNameAsync(string name, int? excludedId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ModelState.AddModelError(nameof(Category.Name), "Informe o nome da categoria.");
            return;
        }

        var normalizedName = name.ToUpper();
        var duplicateExists = await _context.Categories
            .AnyAsync(category =>
                (excludedId == null || category.Id != excludedId.Value) &&
                category.Name.ToUpper() == normalizedName);

        if (duplicateExists)
        {
            ModelState.AddModelError(nameof(Category.Name), "Já existe uma categoria com esse nome.");
        }
    }

    public sealed class CategoryListItem
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public int ProductCount { get; init; }
    }

    public sealed class CategoryDeleteViewModel
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public int ProductCount { get; init; }
    }
}
