namespace LojaVirtual.Models
{
    public class ProductMedia
    {
        public int Id { get; set; }

        public string Url { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public int ProductId { get; set; }

        public Product? Product { get; set; }
    }
}