using LojaVirtual.Models;

namespace LojaVirtual.ViewModels;

public class HomeViewModel
{
    public IEnumerable<Product> FeaturedProducts { get; set; }
}