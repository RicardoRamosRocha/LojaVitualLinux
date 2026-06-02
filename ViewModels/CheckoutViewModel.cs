using System.ComponentModel.DataAnnotations;

namespace LojaVirtual.ViewModels;

public class CheckoutViewModel
{
    [Display(Name = "Nome Completo")]
    [Required(ErrorMessage = "O nome do cliente é obrigatório.")]
    public string CustomerName { get; set; } = "";

    [Display(Name = "E-mail")]
    [Required(ErrorMessage = "O e-mail do cliente é obrigatório.")]
    [EmailAddress(ErrorMessage = "O e-mail do cliente é inválido.")]
    public string Email { get; set; } = "";

    [Display(Name = "Telefone")]
    [Required(ErrorMessage = "O telefone do cliente é obrigatório.")]
    public string Phone { get; set; } = "";

    [Display(Name = "Endereço")]
    [Required(ErrorMessage = "O endereço do cliente é obrigatório.")]
    public string Address { get; set; } = "";

    [Display(Name = "Total")]
    public decimal Total { get; set; }
}
