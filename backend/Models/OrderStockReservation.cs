namespace LojaVirtual.Models;

public class OrderStockReservation
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public int ProductId { get; set; }

    public int Quantity { get; set; }

    public string Status { get; set; } = "Reservada";

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime ExpiresAt { get; set; }

    public DateTime? ReleasedAt { get; set; }

    public Order Order { get; set; } = null!;

    public Product Product { get; set; } = null!;
}
