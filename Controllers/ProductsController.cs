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
            var products = await _context.Products
                .Include(p => p.ProductMedias)
                .ToListAsync();

            return View(products);
        }
        // GET: Products/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
                return NotFound();

            var product = await _context.Products
                .Include(p => p.ProductMedias)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (product == null)
                return NotFound();

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
          List<IFormFile>? imageFiles,
          List<IFormFile>? videoFiles)
        {
            try
            {
                _context.Products.Add(product);

                await _context.SaveChangesAsync();

                // =========================
                // IMAGENS
                // =========================
                if (imageFiles != null)
                {
                    foreach (var imageFile in imageFiles)
                    {
                        if (imageFile.Length > 0)
                        {
                            var fileName =
                                Guid.NewGuid() +
                                Path.GetExtension(imageFile.FileName);

                            var folder =
                                Path.Combine(
                                    Directory.GetCurrentDirectory(),
                                    "wwwroot/images/products");

                            Directory.CreateDirectory(folder);

                            var path =
                                Path.Combine(folder, fileName);

                            using (var stream = new FileStream(path, FileMode.Create))
                            {
                                await imageFile.CopyToAsync(stream);
                            }

                            var media = new ProductMedia
                            {
                                ProductId = product.Id,
                                Url = "/images/products/" + fileName,
                                Type = "image"
                            };

                            _context.ProductMedias.Add(media);
                        }
                    }
                }

                // =========================
                // VIDEOS
                // =========================
                if (videoFiles != null)
                {
                    foreach (var videoFile in videoFiles)
                    {
                        if (videoFile.Length > 0)
                        {
                            var fileName =
                                Guid.NewGuid() +
                                Path.GetExtension(videoFile.FileName);

                            var folder =
                                Path.Combine(
                                    Directory.GetCurrentDirectory(),
                                    "wwwroot/videos/products");

                            Directory.CreateDirectory(folder);

                            var path =
                                Path.Combine(folder, fileName);

                            using (var stream = new FileStream(path, FileMode.Create))
                            {
                                await videoFile.CopyToAsync(stream);
                            }

                            var media = new ProductMedia
                            {
                                ProductId = product.Id,
                                Url = "/videos/products/" + fileName,
                                Type = "video"
                            };

                            _context.ProductMedias.Add(media);
                        }
                    }
                }

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
       List<IFormFile>? imageFiles,
       List<IFormFile>? videoFiles)
        {
            var existing = await _context.Products
                .Include(p => p.ProductMedias)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (existing == null)
                return NotFound();

            // =========================
            // UPDATE DADOS
            // =========================
            existing.Name = product.Name;
            existing.Description = product.Description;
            existing.Price = product.Price;
            existing.Stock = product.Stock;
            existing.CategoryId = product.CategoryId;

            // =========================
            // NOVAS IMAGENS
            // =========================
            if (imageFiles != null)
            {
                foreach (var imageFile in imageFiles)
                {
                    if (imageFile.Length > 0)
                    {
                        var fileName =
                            Guid.NewGuid() +
                            Path.GetExtension(imageFile.FileName);

                        var folder = Path.Combine(
                            _webHostEnvironment.WebRootPath,
                            "images/products");

                        Directory.CreateDirectory(folder);

                        var path = Path.Combine(folder, fileName);

                        using (var stream = new FileStream(path, FileMode.Create))
                        {
                            await imageFile.CopyToAsync(stream);
                        }

                        var media = new ProductMedia
                        {
                            ProductId = existing.Id,
                            Url = "/images/products/" + fileName,
                            Type = "image"
                        };

                        _context.ProductMedias.Add(media);
                    }
                }
            }

            // =========================
            // NOVOS VIDEOS
            // =========================
            if (videoFiles != null)
            {
                foreach (var videoFile in videoFiles)
                {
                    if (videoFile.Length > 0)
                    {
                        var fileName =
                            Guid.NewGuid() +
                            Path.GetExtension(videoFile.FileName);

                        var folder = Path.Combine(
                            _webHostEnvironment.WebRootPath,
                            "videos/products");

                        Directory.CreateDirectory(folder);

                        var path = Path.Combine(folder, fileName);

                        using (var stream = new FileStream(path, FileMode.Create))
                        {
                            await videoFile.CopyToAsync(stream);
                        }

                        var media = new ProductMedia
                        {
                            ProductId = existing.Id,
                            Url = "/videos/products/" + fileName,
                            Type = "video"
                        };

                        _context.ProductMedias.Add(media);
                    }
                }
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
            var product = await _context.Products
                .Include(p => p.ProductMedias)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
                return NotFound();

            // REMOVE MIDIAS DO DISCO
            foreach (var media in product.ProductMedias)
            {
                var mediaPath = Path.Combine(
                    _webHostEnvironment.WebRootPath,
                    media.Url.TrimStart('/'));

                if (System.IO.File.Exists(mediaPath))
                {
                    System.IO.File.Delete(mediaPath);
                }
            }

            // REMOVE MIDIAS DO BANCO
            _context.ProductMedias.RemoveRange(product.ProductMedias);

            // REMOVE PRODUTO
            _context.Products.Remove(product);

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}