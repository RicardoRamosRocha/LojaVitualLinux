namespace LojaVirtual.Models
{
    public enum OrderStatus
    {
        Pending = 1,
        AwaitingPayment = 2,
        Paid = 3,
        Processing = 4,
        Shipped = 5,
        Delivered = 6,
        Cancelled = 7
    }
}