using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using LojaVirtual.Data;
using LojaVirtual.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LojaVirtual.Services;

public sealed record OrderRecoveryRequestResult(bool Accepted);

public sealed record OrderRecoveryConfirmationResult(bool Confirmed);

public interface IOrderRecoveryService
{
    Task<OrderRecoveryRequestResult> RequestAsync(
        string email,
        CancellationToken cancellationToken = default);

    Task<OrderRecoveryConfirmationResult> ConfirmAsync(
        int orderId,
        string token,
        HttpRequest request,
        HttpResponse response,
        CancellationToken cancellationToken = default);
}

public sealed class OrderRecoveryService : IOrderRecoveryService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);
    private readonly AppDbContext _context;
    private readonly IOrderAccessCredentialService _orderAccess;
    private readonly ITransactionalEmailSender _emailSender;
    private readonly IConfiguration? _configuration;

    public OrderRecoveryService(
        AppDbContext context,
        IOrderAccessCredentialService orderAccess,
        ITransactionalEmailSender emailSender,
        IConfiguration? configuration = null)
    {
        _context = context;
        _orderAccess = orderAccess;
        _emailSender = emailSender;
        _configuration = configuration;
    }

    public async Task<OrderRecoveryRequestResult> RequestAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedEmail) || !IsValidEmail(normalizedEmail))
        {
            return new OrderRecoveryRequestResult(true);
        }

        var orders = await _context.Orders
            .Where(item => item.Email.ToLower() == normalizedEmail)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
        if (orders.Count == 0) return new OrderRecoveryRequestResult(true);

        var now = DateTime.UtcNow;
        var messages = new List<string>(orders.Count);
        foreach (var order in orders)
        {
            var rawToken = GenerateToken();
            _context.OrderAccessRecoveryTokens.Add(new OrderAccessRecoveryToken
            {
                OrderId = order.Id,
                TokenHash = HashToken(rawToken),
                CreatedAtUtc = now,
                ExpiresAtUtc = now.Add(TokenLifetime)
            });

            var recoveryLink = BuildRecoveryLink(order.Id, rawToken);
            messages.Add(recoveryLink is null
                ? $"<li>Pedido {order.Id}: use o código <strong>{rawToken}</strong>.</li>"
                : $"<li><a href=\"{recoveryLink}\">Acessar pedido {order.Id}</a></li>");
        }
        await _context.SaveChangesAsync(cancellationToken);

        var message = new TransactionalEmailMessage(
            normalizedEmail,
            "Recuperação de acesso aos seus pedidos",
            $"<p>Use um dos links abaixo para recuperar o acesso aos seus pedidos:</p><ul>{string.Join(string.Empty, messages)}</ul><p>Os links expiram em 15 minutos e podem ser usados uma única vez.</p>");
        try
        {
            await _emailSender.SendAsync(message, cancellationToken);
        }
        catch (TransactionalEmailException)
        {
            // Do not reveal provider details or change the generic response.
        }

        return new OrderRecoveryRequestResult(true);
    }

    public async Task<OrderRecoveryConfirmationResult> ConfirmAsync(
        int orderId,
        string token,
        HttpRequest request,
        HttpResponse response,
        CancellationToken cancellationToken = default)
    {
        if (orderId <= 0 || string.IsNullOrWhiteSpace(token) || token.Length > 128)
        {
            return new OrderRecoveryConfirmationResult(false);
        }

        var now = DateTime.UtcNow;
        await using var transaction = await _context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);

        var tokenHash = HashToken(token);
        var consumed = await _context.OrderAccessRecoveryTokens
            .Where(item => item.OrderId == orderId &&
                           item.TokenHash == tokenHash &&
                           item.UsedAtUtc == null &&
                           item.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.UsedAtUtc, now), cancellationToken);
        if (consumed != 1) return new OrderRecoveryConfirmationResult(false);

        var prepared = await _orderAccess.PrepareAsync(orderId, request, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _orderAccess.WriteCookie(response, prepared);
        return new OrderRecoveryConfirmationResult(true);
    }

    private static bool IsValidEmail(string email) =>
        email.Length <= 180 && email.Contains('@', StringComparison.Ordinal) &&
        email.IndexOf('@') > 0 && email.IndexOf('@') < email.Length - 1;

    private string? BuildRecoveryLink(int orderId, string rawToken)
    {
        var storefrontUrl = _configuration?["Storefront:BaseUrl"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(storefrontUrl) ||
            !Uri.TryCreate(storefrontUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        return $"{storefrontUrl}/pedidos?recoveryOrderId={orderId}&recoveryToken={Uri.EscapeDataString(rawToken)}";
    }

    private static string GenerateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');

    private static string HashToken(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}

public sealed class OrderRecoveryRateLimiter
{
    private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.Ordinal);
    private readonly TimeSpan _window = TimeSpan.FromMinutes(15);
    private const int MaxAttempts = 5;

    public bool TryAcquire(string clientAddress, string identifier)
    {
        var key = $"{clientAddress}|{identifier}";
        var now = DateTimeOffset.UtcNow;
        while (true)
        {
            var current = _windows.GetOrAdd(key, _ => new Window(now, 0));
            if (now - current.Start >= _window)
            {
                if (_windows.TryUpdate(key, new Window(now, 1), current)) return true;
                continue;
            }

            if (current.Count >= MaxAttempts) return false;
            if (_windows.TryUpdate(key, current with { Count = current.Count + 1 }, current)) return true;
        }
    }

    private sealed record Window(DateTimeOffset Start, int Count);
}
