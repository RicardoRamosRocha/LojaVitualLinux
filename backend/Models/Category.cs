using System.ComponentModel.DataAnnotations;

namespace LojaVirtual.Models;

public class Category
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Informe o nome da categoria.")]
    public string Name { get; set; } = string.Empty;

    public ICollection<Product> Products { get; set; }
        = new List<Product>();

}
