using LojaVirtual.Models;

namespace LojaVirtual.ViewModels
{
    public class HomeViewModel
    {
        public List<Product> FeaturedProducts { get; set; }
            = new();
    }
}