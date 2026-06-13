using LojaVirtual.Models;

namespace LojaVirtual.ViewModels
{
    public class HomeViewModel
    {
        public List<Product> FeaturedProducts { get; set; }
            = new();

        public List<Product> NewCollectionProducts { get; set; }
            = new();

        public List<Product> BestSellerProducts { get; set; }
            = new();
    }
}