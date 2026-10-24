namespace LojaVirtual.Models;

/// <summary>
/// Minimal session representation used by the REST cart.
/// Kept separate from the MVC "cart" session value for compatibility.
/// </summary>
public sealed class ApiCartItemSession
{
    public int ProductId { get; set; }

    public int Quantity { get; set; }
}
