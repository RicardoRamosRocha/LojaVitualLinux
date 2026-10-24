using System.ComponentModel.DataAnnotations;

namespace LojaVirtual.Models
{
    public class Product
    {
        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        [DisplayFormat(DataFormatString = "{0:F2}")]
        public decimal Price { get; set; }

        public int Stock { get; set; }

        public bool IsActive { get; set; } = true;

        public bool IsFeatured { get; set; } = false;

        public bool IsNewCollection { get; set; } = false;

        public bool IsBestSeller { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // COMPATIBILIDADE TEMPORÁRIA
        public string? ImageUrl { get; set; }

        public string? VideoUrl { get; set; }

        public int? CategoryId { get; set; } 

         public Category? Category { get; set; }

        // GALERIA
        public ICollection<ProductMedia> ProductMedias { get; set; }
            = new List<ProductMedia>();

        public ICollection<OrderItem> OrderItems { get; set; }
          = new List<OrderItem>();
         
    }
}