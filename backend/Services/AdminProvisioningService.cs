using LojaVirtual.Models;
using Microsoft.AspNetCore.Identity;

namespace LojaVirtual.Web.Services;

public sealed class AdminProvisioningService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public AdminProvisioningService(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task RunInteractiveAsync()
    {
        if (!Environment.UserInteractive)
        {
            throw new InvalidOperationException("O provisionamento exige um terminal interativo.");
        }

        Console.Write("E-mail do administrador: ");
        var email = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("O e-mail é obrigatório.");
        }

        var user = await _userManager.FindByEmailAsync(email);
        if (user is not null)
        {
            if (await _userManager.IsInRoleAsync(user, "Admin"))
            {
                Console.WriteLine("O usuário já possui a role Admin; nenhuma alteração foi feita.");
                return;
            }

            throw new InvalidOperationException(
                "O usuário já existe e não é Admin. Nenhuma elevação automática foi feita.");
        }

        Console.Write("Senha (não será exibida): ");
        var password = ReadSecret();
        Console.WriteLine();

        if (!await _roleManager.RoleExistsAsync("Admin"))
        {
            var roleResult = await _roleManager.CreateAsync(new IdentityRole("Admin"));
            EnsureSucceeded(roleResult, "Não foi possível criar a role Admin.");
        }

        user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };

        var userResult = await _userManager.CreateAsync(user, password);
        EnsureSucceeded(userResult, "Não foi possível criar o administrador.");

        var roleAssignment = await _userManager.AddToRoleAsync(user, "Admin");
        EnsureSucceeded(roleAssignment, "Não foi possível atribuir a role Admin.");

        Console.WriteLine("Administrador criado e associado explicitamente à role Admin.");
    }

    private static string ReadSecret()
    {
        var chars = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (chars.Count > 0)
                {
                    chars.RemoveAt(chars.Count - 1);
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                chars.Add(key.KeyChar);
            }
        }

        return new string(chars.ToArray());
    }

    private static void EnsureSucceeded(IdentityResult result, string message)
    {
        if (!result.Succeeded)
        {
            var errors = string.Join(
                "; ",
                result.Errors.Select(error => $"{error.Code}: {error.Description}"));

            throw new InvalidOperationException($"{message} Motivo(s): {errors}");
        }
    }
}
