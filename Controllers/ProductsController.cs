using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using LojaVirtual.Data;
using LojaVirtual.Models;
using Microsoft.AspNetCore.Hosting;

namespace LojaVirtual.Controllers
{
    public class ProductsController : Controller
    {
        private readonly AppDbContext _context;

        private readonly IWebHostEnvironment _webHostEnvironment;

        public ProductsController(AppDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        // GET: Products
        public async Task<IActionResult> Index()
        {
            return View(await _context.Products.ToListAsync());
        }

        // GET: Products/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var product = await _context.Products
                .FirstOrDefaultAsync(m => m.Id == id);
            if (product == null)
            {
                return NotFound();
            }

            return View(product);
        }

        // GET: Products/Create
        public IActionResult Create()
        {
            return View();
        }

  [HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Create(
    Product product,
    IFormFile? imageFile,
    IFormFile? videoFile)
{
    try
    {
        // =========================
        // IMAGEM
        // =========================
        if (imageFile != null && imageFile.Length > 0)
        {
            var imageName =
                Guid.NewGuid() + Path.GetExtension(imageFile.FileName);

            var imageFolder =
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot/images/products");

            // cria pasta automaticamente
            Directory.CreateDirectory(imageFolder);

            var imagePath =
                Path.Combine(imageFolder, imageName);

            using (var stream = new FileStream(imagePath, FileMode.Create))
            {
                await imageFile.CopyToAsync(stream);
            }

            product.ImageUrl =
                "/images/products/" + imageName;
        }

        // =========================
        // VIDEO
        // =========================
        if (videoFile != null && videoFile.Length > 0)
        {
            var videoName =
                Guid.NewGuid() + Path.GetExtension(videoFile.FileName);

            var videoFolder =
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot/videos/products");

            // cria pasta automaticamente
            Directory.CreateDirectory(videoFolder);

            var videoPath =
                Path.Combine(videoFolder, videoName);

            using (var stream = new FileStream(videoPath, FileMode.Create))
            {
                await videoFile.CopyToAsync(stream);
            }

            product.VideoUrl =
                "/videos/products/" + videoName;
        }

        _context.Products.Add(product);

        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }
    catch (Exception ex)
    {
        return Content(ex.ToString());
    }
}

        // GET: Products/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var product = await _context.Products.FindAsync(id);
            if (product == null)
            {
                return NotFound();
            }
            return View(product);
        }

        // POST: Products/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
     int id,
     Product product,
     IFormFile imageFile)
        {
            var existing = await _context.Products.FindAsync(id);

            if (existing == null)
                return NotFound();

            existing.Name = product.Name;
            existing.Price = product.Price;
            existing.Description = product.Description;
            existing.Stock = product.Stock;
            existing.CategoryId = product.CategoryId;

            // NOVA IMAGEM
            if (imageFile != null && imageFile.Length > 0)
            {
                // REMOVE IMAGEM ANTIGA
                if (!string.IsNullOrEmpty(existing.ImageUrl))
                {
                    var oldImagePath = Path.Combine(
                        _webHostEnvironment.WebRootPath,
                        existing.ImageUrl.TrimStart('/'));

                    if (System.IO.File.Exists(oldImagePath))
                    {
                        System.IO.File.Delete(oldImagePath);
                    }
                }

                // CRIA NOME NOVO
                var fileName = Guid.NewGuid() +
                               Path.GetExtension(imageFile.FileName);

                // PASTA
                var folder = Path.Combine(
                    _webHostEnvironment.WebRootPath,
                    "images/products");

                // GARANTE EXISTÊNCIA
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                // CAMINHO FINAL
                var path = Path.Combine(folder, fileName);

                // SALVA NOVA IMAGEM
                using (var stream = new FileStream(path, FileMode.Create))
                {
                    await imageFile.CopyToAsync(stream);
                }

                // SALVA URL NO BANCO
                existing.ImageUrl = "/images/products/" + fileName;
            }

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
        // GET: Products/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var product = await _context.Products
                .FirstOrDefaultAsync(m => m.Id == id);
            if (product == null)
            {
                return NotFound();
            }

            return View(product);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var product = await _context.Products.FindAsync(id);

            if (product != null)
            {
                // Remove imagem física
                if (!string.IsNullOrEmpty(product.ImageUrl))
                {
                    var imagePath = Path.Combine(
                        _webHostEnvironment.WebRootPath,
                        product.ImageUrl.TrimStart('/'));

                    if (System.IO.File.Exists(imagePath))
                    {
                        System.IO.File.Delete(imagePath);
                    }
                }

                // Remove produto do banco
                _context.Products.Remove(product);

                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }
    }
    
}