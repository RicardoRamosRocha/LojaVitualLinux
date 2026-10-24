using System.ComponentModel.DataAnnotations;

namespace LojaVirtual.ViewModels;

public sealed class AdminLoginViewModel
{
    [Required]
    [EmailAddress]
    [Display(Name = "E-mail")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Senha")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Lembrar de mim")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}
