using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using LojaVirtual.Data;
using LojaVirtual.Models;
using Microsoft.EntityFrameworkCore;

namespace LojaVirtual.Services;

public sealed record OrderRecoveryRequestResult(bool Accepted);

public sealed record OrderRecoveryConfirmationResult(bool Confirmed);

public interface IOrderRecoveryService
{
    Task<OrderRecoveryRequestResult> RequestAsync(
        int orderId,
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

    public OrderRecoveryService(
        AppDbContext context,
        IOrderAccessCredentialService orderAccess,
        ITransactionalEmailSender emailSender)
    {
        _context = context;
        _orderAccess = orderAccess;
        _emailSender = emailSender;
    }

    public async Task<OrderRecoveryRequestResult> RequestAsync(
        int orderId,
        string email,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        if (orderId <= 0 || string.IsNullOrWhiteSpace(normalizedEmail) || !IsValidEmail(normalizedEmail))
        {
            return new OrderRecoveryRequestResult(true);
        }

        var order = await _context.Orders
            .SingleOrDefaultAsync(item => item.Id == orderId && item.Email.ToLower() == normalizedEmail, cancellationToken);
        if (order is null) return new OrderRecoveryRequestResult(true);

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');
        var now = DateTime.UtcNow;
        _context.OrderAccessRecoveryTokens.Add(new OrderAccessRecoveryToken
        {
            OrderId = order.Id,
            TokenHash = HashToken(rawToken),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(TokenLifetime)
        });
        await _context.SaveChangesAsync(cancellationToken);

        var message = new TransactionalEmailMessage(
            order.Email,
            "Recuperação de acesso aos seus pedidos",
            $"<p>Use este código temporário para recuperar o acesso ao pedido {order.Id}:</p><p><strong>{rawToken}</strong></p><p>O código expira em 15 minutos e pode ser usado uma única vez.</p>");
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
