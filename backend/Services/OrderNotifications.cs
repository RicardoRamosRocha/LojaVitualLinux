using System.Net;
using LojaVirtual.Data;
using LojaVirtual.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LojaVirtual.Services;

public static class OrderNotificationEventTypes
{
    public const string OrderCreated = "order_created";
    public const string PaymentApprovedPreparing = "payment_approved_preparing";
    public const string OrderShipped = "order_shipped";
    public const string OrderDelivered = "order_delivered";
}

public sealed class OrderNotificationsOptions
{
    public bool Enabled { get; set; }
}

public interface IOrderNotificationOutbox
{
    Task EnqueueAsync(Order order, string eventType, CancellationToken cancellationToken = default);
}

public sealed class NoopOrderNotificationOutbox : IOrderNotificationOutbox
{
    public Task EnqueueAsync(Order order, string eventType, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

public sealed class OrderNotificationOutboxService : IOrderNotificationOutbox
{
    private readonly AppDbContext _context;

    public OrderNotificationOutboxService(AppDbContext context) => _context = context;

    public async Task EnqueueAsync(
        Order order,
        string eventType,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidEmail(order.Email)) return;

        if (_context.OrderNotificationOutbox.Local.Any(item =>
                item.OrderId == order.Id && item.EventType == eventType))
        {
            return;
        }

        var exists = await _context.OrderNotificationOutbox
            .AnyAsync(item => item.OrderId == order.Id && item.EventType == eventType, cancellationToken);
        if (exists) return;

        _context.OrderNotificationOutbox.Add(new OrderNotificationOutbox
        {
            OrderId = order.Id,
            EventType = eventType,
            CreatedAtUtc = DateTime.UtcNow,
            NextAttemptAtUtc = DateTime.UtcNow
        });
    }

    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;

        var normalized = email.Trim();
        return normalized.Length <= 180
            && normalized.Contains('@', StringComparison.Ordinal)
            && normalized.IndexOf('@') > 0
            && normalized.IndexOf('@') < normalized.Length - 1;
    }
}

public interface IOrderNotificationComposer
{
    bool TryCompose(Order order, string eventType, out TransactionalEmailMessage? message);
}

public sealed class OrderNotificationComposer : IOrderNotificationComposer
{
    public bool TryCompose(Order order, string eventType, out TransactionalEmailMessage? message)
    {
        message = null;
        if (!OrderNotificationOutboxService.IsValidEmail(order.Email)) return false;

        var customerName = WebUtility.HtmlEncode(order.CustomerName);
        var orderId = order.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var total = order.Total.ToString("C", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        var subject = eventType switch
        {
            OrderNotificationEventTypes.OrderCreated => $"Pedido #{orderId} recebido — Angel Acessórios",
            OrderNotificationEventTypes.PaymentApprovedPreparing => $"Pedido #{orderId} em preparação — Angel Acessórios",
            OrderNotificationEventTypes.OrderShipped => $"Pedido #{orderId} enviado — Angel Acessórios",
            OrderNotificationEventTypes.OrderDelivered => $"Pedido #{orderId} entregue — Angel Acessórios",
            _ => null
        };

        var body = eventType switch
        {
            OrderNotificationEventTypes.OrderCreated =>
                $"<p>Olá, {customerName}!</p><p>Recebemos o seu pedido <strong>#{orderId}</strong>, no valor de <strong>{total}</strong>.</p><p>O pagamento ainda aguarda confirmação.</p>",
            OrderNotificationEventTypes.PaymentApprovedPreparing =>
                $"<p>Olá, {customerName}!</p><p>O pagamento do pedido <strong>#{orderId}</strong> foi aprovado e o pedido já está <strong>em preparação</strong>.</p>",
            OrderNotificationEventTypes.OrderShipped =>
                $"<p>Olá, {customerName}!</p><p>O pedido <strong>#{orderId}</strong> foi enviado.</p>",
            OrderNotificationEventTypes.OrderDelivered =>
                $"<p>Olá, {customerName}!</p><p>O pedido <strong>#{orderId}</strong> foi marcado como entregue.</p>",
            _ => null
        };

        if (subject is null || body is null) return false;

        message = new TransactionalEmailMessage(
            order.Email.Trim(),
            subject,
            $"<div><h2>Angel Acessórios</h2>{body}<p>Obrigado por comprar conosco.</p></div>");
        return true;
    }
}

public sealed class OrderNotificationOutboxWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ITransactionalEmailSender _emailSender;
    private readonly IOrderNotificationComposer _composer;
    private readonly IOptions<OrderNotificationsOptions> _notificationOptions;
    private readonly IOptions<ResendOptions> _resendOptions;
    private readonly ILogger<OrderNotificationOutboxWorker> _logger;

    public OrderNotificationOutboxWorker(
        IDbContextFactory<AppDbContext> contextFactory,
        ITransactionalEmailSender emailSender,
        IOrderNotificationComposer composer,
        IOptions<OrderNotificationsOptions> notificationOptions,
        IOptions<ResendOptions> resendOptions,
        ILogger<OrderNotificationOutboxWorker> logger)
    {
        _contextFactory = contextFactory;
        _emailSender = emailSender;
        _composer = composer;
        _notificationOptions = notificationOptions;
        _resendOptions = resendOptions;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var claimed = await ProcessOneAsync(stoppingToken);
                if (!claimed) await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Falha no processador da outbox de notificações.");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    private async Task<bool> ProcessOneAsync(CancellationToken cancellationToken)
    {
        if (!_notificationOptions.Value.Enabled || !_resendOptions.Value.Enabled) return false;

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var candidate = await context.OrderNotificationOutbox
            .AsNoTracking()
            .Where(item => item.ProcessedAtUtc == null
                && item.NextAttemptAtUtc <= now
                && (item.LeaseExpiresAtUtc == null || item.LeaseExpiresAtUtc < now))
            .OrderBy(item => item.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (candidate is null) return false;

        var leaseToken = Guid.NewGuid().ToString("N");
        var claimed = await context.OrderNotificationOutbox
            .Where(item => item.Id == candidate.Id
                && item.ProcessedAtUtc == null
                && (item.LeaseExpiresAtUtc == null || item.LeaseExpiresAtUtc < now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.LeaseToken, leaseToken)
                .SetProperty(item => item.LeaseExpiresAtUtc, now.Add(LeaseDuration))
                .SetProperty(item => item.AttemptCount, item => item.AttemptCount + 1), cancellationToken);
        if (claimed != 1) return true;

        var order = await context.Orders
            .Include(item => item.Items)
            .SingleAsync(item => item.Id == candidate.OrderId, cancellationToken);

        if (!_composer.TryCompose(order, candidate.EventType, out var message) || message is null)
        {
            await MarkProcessedAsync(context, candidate.Id, leaseToken, "recipient_invalid", cancellationToken);
            return true;
        }

        try
        {
            await _emailSender.SendAsync(message, cancellationToken);
            await MarkProcessedAsync(context, candidate.Id, leaseToken, null, cancellationToken);
        }
        catch (TransactionalEmailException)
        {
            await RescheduleAsync(context, candidate.Id, leaseToken, candidate.AttemptCount + 1, cancellationToken);
            _logger.LogWarning(
                "Notificação {EventType} do pedido {OrderId} será tentada novamente após falha do provedor.",
                candidate.EventType,
                candidate.OrderId);
        }

        return true;
    }

    private static Task<int> MarkProcessedAsync(
        AppDbContext context,
        long id,
        string leaseToken,
        string? error,
        CancellationToken cancellationToken) =>
        context.OrderNotificationOutbox
            .Where(item => item.Id == id && item.LeaseToken == leaseToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.ProcessedAtUtc, DateTime.UtcNow)
                .SetProperty(item => item.LeaseToken, (string?)null)
                .SetProperty(item => item.LeaseExpiresAtUtc, (DateTime?)null)
                .SetProperty(item => item.LastError, error), cancellationToken);

    private static Task<int> RescheduleAsync(
        AppDbContext context,
        long id,
        string leaseToken,
        int attemptCount,
        CancellationToken cancellationToken)
    {
        var delaySeconds = Math.Min(3600, Math.Pow(2, Math.Min(attemptCount, 10)));
        return context.OrderNotificationOutbox
            .Where(item => item.Id == id && item.LeaseToken == leaseToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.NextAttemptAtUtc, DateTime.UtcNow.AddSeconds(delaySeconds))
                .SetProperty(item => item.LeaseToken, (string?)null)
                .SetProperty(item => item.LeaseExpiresAtUtc, (DateTime?)null)
                .SetProperty(item => item.LastError, "provider_failure"), cancellationToken);
    }
}
