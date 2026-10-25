using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LojaVirtual.Data;
using LojaVirtual.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace LojaVirtual.Services;

public interface IOrderAccessCredentialService
{
    Task<PreparedOrderAccess> PrepareAsync(int orderId, HttpRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuthorizedOrderAccess>> ResolveAsync(HttpRequest request, CancellationToken cancellationToken = default);
    Task RevokeAllAsync(HttpRequest request, CancellationToken cancellationToken = default);
    void WriteCookie(HttpResponse response, PreparedOrderAccess access);
    void DeleteCookie(HttpResponse response);
}

public sealed record PreparedOrderAccess(
    IReadOnlyList<string> RawTokens,
    DateTime CookieExpiresAt,
    OrderAccessCredential? NewCredential);

public sealed record AuthorizedOrderAccess(string RawToken, OrderAccessCredential Credential);

public sealed class OrderAccessCredentialService : IOrderAccessCredentialService
{
    public const string CookieName = "Angel.OrderAccess";

    private const int TokenBytes = 32;
    private const int MaxTokensPerCookie = 8;
    private const int CredentialLifetimeDays = 30;

    private readonly AppDbContext _context;
    private readonly IDataProtector _protector;

    public OrderAccessCredentialService(AppDbContext context, IDataProtectionProvider dataProtectionProvider)
    {
        _context = context;
        _protector = dataProtectionProvider.CreateProtector("AngelAcessorios.OrderAccessCookie.v1");
    }

    public async Task<PreparedOrderAccess> PrepareAsync(
        int orderId,
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        var authorized = await ResolveAsync(request, cancellationToken);
        var existing = authorized.FirstOrDefault(item => item.Credential.OrderId == orderId);
        if (existing is not null)
        {
            return new PreparedOrderAccess(
                authorized.Select(item => item.RawToken).ToArray(),
                authorized.Max(item => item.Credential.ExpiresAt),
                null);
        }

        var rawToken = GenerateToken();
        var now = DateTime.UtcNow;
        var credential = new OrderAccessCredential
        {
            OrderId = orderId,
            TokenHash = HashToken(rawToken),
            CreatedAt = now,
            ExpiresAt = now.AddDays(CredentialLifetimeDays)
        };

        _context.OrderAccessCredentials.Add(credential);

        var rawTokens = authorized
            .Select(item => item.RawToken)
            .Append(rawToken)
            .TakeLast(MaxTokensPerCookie)
            .ToArray();

        return new PreparedOrderAccess(rawTokens, credential.ExpiresAt, credential);
    }

    public async Task<IReadOnlyList<AuthorizedOrderAccess>> ResolveAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        var rawTokens = ReadRawTokens(request);
        if (rawTokens.Count == 0)
        {
            return [];
        }

        var hashes = rawTokens.Select(HashToken).Distinct().ToArray();
        var credentials = await _context.OrderAccessCredentials
            .Where(credential => hashes.Contains(credential.TokenHash) &&
                                 credential.RevokedAt == null &&
                                 credential.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        var byHash = credentials.ToDictionary(credential => credential.TokenHash, StringComparer.Ordinal);
        return rawTokens
            .Select(rawToken => byHash.TryGetValue(HashToken(rawToken), out var credential)
                ? new AuthorizedOrderAccess(rawToken, credential)
                : null)
            .Where(item => item is not null)
            .Cast<AuthorizedOrderAccess>()
            .ToArray();
    }

    public async Task RevokeAllAsync(HttpRequest request, CancellationToken cancellationToken = default)
    {
        var authorized = await ResolveAsync(request, cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var item in authorized)
        {
            item.Credential.RevokedAt = now;
        }

        if (authorized.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public void WriteCookie(HttpResponse response, PreparedOrderAccess access)
    {
        var protectedPayload = _protector.Protect(JsonSerializer.Serialize(new CookiePayload(access.RawTokens)));
        response.Cookies.Append(CookieName, protectedPayload, CreateCookieOptions(response.HttpContext, access.CookieExpiresAt));
    }

    public void DeleteCookie(HttpResponse response)
    {
        response.Cookies.Delete(CookieName, CreateCookieOptions(response.HttpContext, null));
    }

    private IReadOnlyList<string> ReadRawTokens(HttpRequest request)
    {
        if (!request.Cookies.TryGetValue(CookieName, out var protectedPayload) || string.IsNullOrWhiteSpace(protectedPayload))
        {
            return [];
        }

        try
        {
            var payload = JsonSerializer.Deserialize<CookiePayload>(_protector.Unprotect(protectedPayload));
            return payload?.Tokens?
                .Where(IsValidToken)
                .Distinct(StringComparer.Ordinal)
                .Take(MaxTokensPerCookie)
                .ToArray() ?? [];
        }
        catch (CryptographicException)
        {
            return [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static CookieOptions CreateCookieOptions(HttpContext context, DateTime? expires)
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = !context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Path = "/api/orders",
            Expires = expires
        };
    }

    private static string GenerateToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes))
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');
    }

    private static string HashToken(string rawToken)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    }

    private static bool IsValidToken(string token)
    {
        return token.Length == 43 && token.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    }

    private sealed record CookiePayload(IReadOnlyList<string> Tokens);
}
