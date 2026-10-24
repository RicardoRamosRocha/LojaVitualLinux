namespace LojaVirtual.Models
{
    public class Address
    {
        public int Id { get; set; }

        public string ZipCode { get; set; } = string.Empty;

        public string Street { get; set; } = string.Empty;

        public string Number { get; set; } = string.Empty;

        public string Neighborhood { get; set; } = string.Empty;

        public string City { get; set; } = string.Empty;

        public string State { get; set; } = string.Empty;
    }
}