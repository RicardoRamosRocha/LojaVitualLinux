namespace LojaVirtual.Models;

public class Product
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public string Description { get; set; } = string.Empty;

    public int Stock { get; set; }

    public string? ImageUrl { get; set; } = string.Empty;

    public string? VideoUrl { get; set; } = string.Empty;

    public int? CategoryId { get; set; }

    public Category? Category { get; set; }
}