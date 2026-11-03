using LojaVirtual.Data;
using LojaVirtual.Models;
using LojaVirtual.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LojaVirtual.Controllers;

[Authorize(Policy = "AdminOnly")]
public class ProductsController : Controller
{
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly ProductImageService _images;
    private readonly CloudinaryOptions _cloudinaryOptions;

    public ProductsController(AppDbContext context, IWebHostEnvironment environment, ProductImageService images, IOptions<CloudinaryOptions> cloudinaryOptions)
    {
        _context = context;
        _environment = environment;
        _images = images;
        _cloudinaryOptions = cloudinaryOptions.Value;
    }

    public async Task<IActionResult> Index() => View(await _context.Products.Include(p => p.ProductMedias).ToListAsync());

    public async Task<IActionResult> Details(int? id)
    {
        if (id is null) return NotFound();
        var product = await _context.Products.Include(p => p.Category).Include(p => p.ProductMedias).FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return NotFound();
        ViewBag.RelatedProducts = await _context.Products.Include(p => p.ProductMedias).Where(p => p.CategoryId == product.CategoryId && p.Id != product.Id).Take(4).ToListAsync();
        return View(product);
    }

    public async Task<IActionResult> Create() { await PopulateCategoriesAsync(); return View(); }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Product product, List<IFormFile>? imageFiles, List<IFormFile>? videoFiles)
    {
        if (!ModelState.IsValid) { await PopulateCategoriesAsync(product.CategoryId); return View(product); }
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var uploaded = new List<StoredImage>();
        var localFiles = new List<string>();
        try
        {
            _context.Products.Add(product);
            await _context.SaveChangesAsync();
            uploaded.AddRange(await _images.UploadAsync(imageFiles, product.Id));
            AddImageMedia(product.Id, uploaded);
            AddVideoMedia(product.Id, videoFiles, localFiles);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["SuccessMessage"] = "Produto cadastrado com sucesso.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception)
        {
            await _images.DeleteQuietlyAsync(uploaded);
            DeleteLocalFiles(localFiles);
            await transaction.RollbackAsync();
            ModelState.AddModelError(string.Empty, "Não foi possível cadastrar o produto. Verifique os arquivos e tente novamente.");
            await PopulateCategoriesAsync(product.CategoryId);
            return View(product);
        }
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null) return NotFound();
        var product = await _context.Products.Include(p => p.ProductMedias).FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return NotFound();
        await PopulateCategoriesAsync(product.CategoryId);
        return View(product);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Product product, List<IFormFile>? imageFiles, List<IFormFile>? videoFiles)
    {
        if (!ModelState.IsValid) { await PopulateCategoriesAsync(product.CategoryId); return View(product); }
        var existing = await _context.Products.Include(p => p.ProductMedias).FirstOrDefaultAsync(p => p.Id == id);
        if (existing is null) return NotFound();
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var uploaded = new List<StoredImage>();
        var localFiles = new List<string>();
        try
        {
            existing.Name = product.Name; existing.Description = product.Description; existing.Price = product.Price;
            existing.Stock = product.Stock; existing.CategoryId = product.CategoryId; existing.IsActive = product.IsActive;
            existing.IsFeatured = product.IsFeatured; existing.IsNewCollection = product.IsNewCollection; existing.IsBestSeller = product.IsBestSeller;
            uploaded.AddRange(await _images.UploadAsync(imageFiles, existing.Id));
            AddImageMedia(existing.Id, uploaded);
            AddVideoMedia(existing.Id, videoFiles, localFiles);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["SuccessMessage"] = "Produto atualizado com sucesso.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception)
        {
            await _images.DeleteQuietlyAsync(uploaded);
            DeleteLocalFiles(localFiles);
            await transaction.RollbackAsync();
            ModelState.AddModelError(string.Empty, "Não foi possível atualizar o produto. Verifique os arquivos e tente novamente.");
            await PopulateCategoriesAsync(product.CategoryId);
            return View(existing);
        }
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null) return NotFound();
        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);
        return product is null ? NotFound() : View(product);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var product = await _context.Products.Include(p => p.ProductMedias).FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return NotFound();
        foreach (var media in product.ProductMedias) await DeleteMediaAssetAsync(media);
        _context.ProductMedias.RemoveRange(product.ProductMedias);
        _context.Products.Remove(product);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteMedia(int id)
    {
        var media = await _context.ProductMedias.FirstOrDefaultAsync(x => x.Id == id);
        if (media is null) return NotFound();
        await DeleteMediaAssetAsync(media);
        _context.ProductMedias.Remove(media);
        await _context.SaveChangesAsync();
        return Redirect(Request.Headers.Referer.ToString());
    }

    private void AddImageMedia(int productId, IEnumerable<StoredImage> images)
    {
        foreach (var image in images) _context.ProductMedias.Add(new ProductMedia { ProductId = productId, Url = image.Url, Type = "image" });
    }

    private void AddVideoMedia(int productId, IEnumerable<IFormFile>? files, ICollection<string> createdFiles)
    {
        foreach (var file in (files ?? []).Where(file => file.Length > 0))
        {
            if (file.Length > 100 * 1024 * 1024 || !string.Equals(file.ContentType, "video/mp4", StringComparison.OrdinalIgnoreCase))
                throw new ImageStorageException("Cada vídeo deve ser MP4 e ter no máximo 100 MB.");
            var folder = Path.Combine(_environment.WebRootPath, "videos/products");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}");
            using var stream = new FileStream(path, FileMode.CreateNew);
            file.CopyTo(stream);
            createdFiles.Add(path);
            _context.ProductMedias.Add(new ProductMedia { ProductId = productId, Url = "/videos/products/" + Path.GetFileName(path), Type = "video" });
        }
    }

    private async Task DeleteMediaAssetAsync(ProductMedia media)
    {
        if (media.Type == "image" && TryGetManagedPublicId(media.Url, out var publicId)) await _images.DeleteAsync(publicId);
        else if (media.Url.StartsWith("/", StringComparison.Ordinal))
        {
            var path = Path.Combine(_environment.WebRootPath, media.Url.TrimStart('/'));
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
    }

    private bool TryGetManagedPublicId(string url, out string publicId)
    {
        publicId = string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("res.cloudinary.com", StringComparison.OrdinalIgnoreCase)) return false;
        var marker = $"/{_cloudinaryOptions.CloudName}/image/upload/"; var index = uri.AbsolutePath.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0) return false;
        var candidate = uri.AbsolutePath[(index + marker.Length)..].Trim('/');
        if (candidate.StartsWith("v", StringComparison.Ordinal) && candidate.IndexOf('/') > 1) candidate = candidate[(candidate.IndexOf('/') + 1)..];
        var extension = Path.GetExtension(candidate);
        if (!candidate.StartsWith("angel-acessorios/products/", StringComparison.Ordinal) || string.IsNullOrEmpty(extension)) return false;
        publicId = candidate[..^extension.Length];
        return true;
    }

    private static void DeleteLocalFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths) if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
    }

    private async Task PopulateCategoriesAsync(int? selectedCategoryId = null)
    {
        var categories = await _context.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
        ViewBag.CategoryId = new SelectList(categories, nameof(Category.Id), nameof(Category.Name), selectedCategoryId);
    }
}
