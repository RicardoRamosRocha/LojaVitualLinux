using LojaVirtual.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LojaVirtual.Controllers;

[ApiController]
[Route("api/products")]
public class ApiProductsController : ControllerBase
{
    private readonly AppDbContext _context;

    public ApiProductsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProductDto>>> GetProducts()
    {
        var products = await _context.Products
            .AsNoTracking()
            .Where(product => product.IsActive)
            .OrderBy(product => product.Id)
            .Select(product => new ProductDto
            {
                Id = product.Id,
                Name = product.Name,
                Description = product.Description,
                Price = product.Price,
                Stock = product.Stock,
                CategoryId = product.CategoryId,
                Category = product.Category == null
                    ? null
                    : new CategoryDto
                    {
                        Id = product.Category.Id,
                        Name = product.Category.Name
                    },
                IsFeatured = product.IsFeatured,
                IsNewCollection = product.IsNewCollection,
                IsBestSeller = product.IsBestSeller,
                ImageUrl = product.ImageUrl,
                VideoUrl = product.VideoUrl,
                ImageUrls = product.ProductMedias
                    .Where(media => media.Type == "image")
                    .Select(media => media.Url)
                    .ToList(),
                VideoUrls = product.ProductMedias
                    .Where(media => media.Type == "video")
                    .Select(media => media.Url)
                    .ToList()
            })
            .ToListAsync();

        return Ok(products);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductDto>> GetProduct(int id)
    {
        var product = await _context.Products
            .AsNoTracking()
            .Where(product => product.IsActive && product.Id == id)
            .Select(product => new ProductDto
            {
                Id = product.Id,
                Name = product.Name,
                Description = product.Description,
                Price = product.Price,
                Stock = product.Stock,
                CategoryId = product.CategoryId,
                Category = product.Category == null
                    ? null
                    : new CategoryDto
                    {
                        Id = product.Category.Id,
                        Name = product.Category.Name
                    },
                IsFeatured = product.IsFeatured,
                IsNewCollection = product.IsNewCollection,
                IsBestSeller = product.IsBestSeller,
                ImageUrl = product.ImageUrl,
                VideoUrl = product.VideoUrl,
                ImageUrls = product.ProductMedias
                    .Where(media => media.Type == "image")
                    .Select(media => media.Url)
                    .ToList(),
                VideoUrls = product.ProductMedias
                    .Where(media => media.Type == "video")
                    .Select(media => media.Url)
                    .ToList()
            })
            .SingleOrDefaultAsync();

        return product is null ? NotFound() : Ok(product);
    }

    public sealed class ProductDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public int? CategoryId { get; set; }
        public CategoryDto? Category { get; set; }
        public bool IsFeatured { get; set; }
        public bool IsNewCollection { get; set; }
        public bool IsBestSeller { get; set; }
        public string? ImageUrl { get; set; }
        public string? VideoUrl { get; set; }
        public List<string> ImageUrls { get; set; } = [];
        public List<string> VideoUrls { get; set; } = [];
    }

    public sealed class CategoryDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
