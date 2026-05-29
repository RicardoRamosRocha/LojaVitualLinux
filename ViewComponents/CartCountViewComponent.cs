using LojaVirtual.Extensions;
using LojaVirtual.Models;
using Microsoft.AspNetCore.Mvc;

namespace LojaVirtual.ViewComponents
{
    public class CartCountViewComponent : ViewComponent
    {
        public IViewComponentResult Invoke()
        {
            var cart = HttpContext.Session
                .GetObjectFromJson<List<CartItem>>("cart")
                ?? new List<CartItem>();

            var totalItems = cart.Sum(x => x.Quantity);

            return View(totalItems);
        }
    }
}