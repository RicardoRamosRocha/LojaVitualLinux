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

        // COMPATIBILIDADE TEMPORÁRIA
        public string? ImageUrl { get; set; }

        public string? VideoUrl { get; set; }

        public int? CategoryId { get; set; }  

        // GALERIA
        public ICollection<ProductMedia> ProductMedias { get; set; }
            = new List<ProductMedia>();
         
    }
}