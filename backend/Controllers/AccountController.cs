using LojaVirtual.Models;
using LojaVirtual.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace LojaVirtual.Controllers;

public sealed class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        return View(new AdminLoginViewModel { ReturnUrl = IsSafeLocalUrl(returnUrl) ? returnUrl : null });
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(AdminLoginViewModel model)
    {
        model.ReturnUrl = IsSafeLocalUrl(model.ReturnUrl) ? model.ReturnUrl : null;
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.FindByEmailAsync(model.Email);
        var isAdmin = user is not null && await _userManager.IsInRoleAsync(user, "Admin");
        var result = isAdmin
            ? await _signInManager.PasswordSignInAsync(
                user!,
                model.Password,
                model.RememberMe,
                lockoutOnFailure: true)
            : Microsoft.AspNetCore.Identity.SignInResult.Failed;

        if (result.Succeeded)
        {
            return Redirect(model.ReturnUrl ?? Url.Action("Index", "Admin")!);
        }

        ModelState.AddModelError(string.Empty, "Credenciais inválidas ou conta sem acesso administrativo.");
        return View(model);
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult AccessDenied() => View();

    private bool IsSafeLocalUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl)
        && returnUrl.StartsWith("/", StringComparison.Ordinal)
        && !returnUrl.StartsWith("//", StringComparison.Ordinal)
        && !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
        && Url.IsLocalUrl(returnUrl);
}
