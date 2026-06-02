namespace LojaVirtual.Models
{
    public class Order
    {
        public int Id { get; set; }

        public string CustomerName { get; set; } = "";

        public string Email { get; set; } = "";

        public string Phone { get; set; } = "";

        public string Address { get; set; } = "";

        public decimal Total { get; set; }

        public string Status { get; set; } = "Pendente";

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public ICollection<OrderItem> Items { get; set; }
            = new List<OrderItem>();

        public string ZipCode { get; set; } = "";    

        public string ShippingMethod { get; set; } = "";
        
    }
}