using System.Data;
using LojaVirtual.Data;
using LojaVirtual.Models;
using LojaVirtual.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace LojaVirtual.Services;

public sealed class OrderPaymentService
{
    private readonly AppDbContext _context;
    private readonly IMercadoPagoService _mercadoPago;
    private readonly IConfiguration _configuration;

    public OrderPaymentService(AppDbContext context, IMercadoPagoService mercadoPago, IConfiguration configuration)
    {
        _context = context;
        _mercadoPago = mercadoPago;
        _configuration = configuration;
    }

    public async Task<OrderPaymentAttempt> EnsurePreferenceAsync(Order order, CancellationToken cancellationToken = default)
    {
        var current = order.PaymentAttempts
            .OrderByDescending(attempt => attempt.AttemptNumber)
            .FirstOrDefault(attempt => attempt.Status is not PaymentStatusWorkflow.Rejected and
                                       not PaymentStatusWorkflow.Cancelled and
                                       not PaymentStatusWorkflow.Expired and
                                       not PaymentStatusWorkflow.Refunded &&
                                       !string.IsNullOrWhiteSpace(attempt.CheckoutUrl));
        if (current is not null) return current;

        var preference = await _mercadoPago.CreatePreferenceAsync(order, cancellationToken);
        var expirationMinutes = _configuration.GetValue("MercadoPago:PreferenceExpirationMinutes", 30);
        var attempt = new OrderPaymentAttempt
        {
            OrderId = order.Id,
            AttemptNumber = order.PaymentAttempts.Select(item => item.AttemptNumber).DefaultIfEmpty(0).Max() + 1,
            PreferenceId = preference.PreferenceId,
            CheckoutUrl = preference.InitPoint,
            Amount = order.Total,
            Currency = "BRL",
            IdempotencyKey = $"order-{order.Id}-attempt-{order.PaymentAttempts.Count + 1}",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(expirationMinutes)
        };

        _context.OrderPaymentAttempts.Add(attempt);
        await _context.SaveChangesAsync(cancellationToken);
        return attempt;
    }

    public async Task<bool> ProcessPaymentAsync(MercadoPagoPaymentResult payment, CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(payment.ExternalReference, out var orderId)) return false;

        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var expiredReservations = await _context.StockReservations
            .Where(item => item.Status == "Reservada" && item.ExpiresAt <= DateTime.Now)
            .ToListAsync(cancellationToken);
        foreach (var reservation in expiredReservations)
        {
            reservation.Status = "Liberada";
            reservation.ReleasedAt = DateTime.Now;
        }

        var order = await _context.Orders
            .Include(item => item.Items).ThenInclude(item => item.Product)
            .Include(item => item.StockReservations)
            .Include(item => item.PaymentAttempts)
            .SingleOrDefaultAsync(item => item.Id == orderId, cancellationToken);
        if (order is null) return false;

        var attempt = order.PaymentAttempts.FirstOrDefault(item => item.PreferenceId == payment.PreferenceId)
            ?? order.PaymentAttempts.OrderByDescending(item => item.AttemptNumber).FirstOrDefault();
        if (attempt is null || (attempt.PreferenceId is not null && payment.PreferenceId is not null && attempt.PreferenceId != payment.PreferenceId)) return false;
        if (payment.Amount != order.Total || !string.Equals(payment.Currency, "BRL", StringComparison.OrdinalIgnoreCase)) return false;
        var incomingStatus = PaymentStatusWorkflow.FromMercadoPago(payment.Status);
        if (attempt.PaymentId == payment.PaymentId)
        {
            if (attempt.Status == PaymentStatusWorkflow.Approved && incomingStatus != PaymentStatusWorkflow.Refunded)
                return true;
            if (attempt.Status == PaymentStatusWorkflow.StockReview && incomingStatus is not PaymentStatusWorkflow.Approved and not PaymentStatusWorkflow.Refunded)
                return true;
            if (PaymentStatusWorkflow.IsTerminal(attempt.Status) && incomingStatus != PaymentStatusWorkflow.Refunded)
                return true;
            if (attempt.Status == PaymentStatusWorkflow.Refunded)
                return true;
        }
        if (attempt.PaymentId is not null && attempt.PaymentId != payment.PaymentId) return false;

        attempt.PaymentId = payment.PaymentId;
        attempt.Status = incomingStatus;
        attempt.UpdatedAtUtc = DateTime.UtcNow;

        if (attempt.Status == PaymentStatusWorkflow.Approved)
        {
            var expected = order.Items.ToDictionary(item => item.ProductId, item => item.Quantity);
            var reserved = order.StockReservations
                .Where(item => item.Status == "Reservada" && item.ExpiresAt > DateTime.Now)
                .GroupBy(item => item.ProductId)
                .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));

            if (expected.Any(item => reserved.GetValueOrDefault(item.Key) < item.Value))
            {
                attempt.Status = PaymentStatusWorkflow.StockReview;
            }
            else if (order.Items.Any(item => item.Product.Stock < item.Quantity))
            {
                attempt.Status = PaymentStatusWorkflow.StockReview;
            }
            else
            {
                foreach (var item in order.Items)
                {
                    item.Product.Stock -= item.Quantity;
                }

                if (attempt.Status == PaymentStatusWorkflow.Approved)
                {
                    foreach (var reservation in order.StockReservations.Where(item => item.Status == "Reservada"))
                    {
                        reservation.Status = "Confirmada";
                        reservation.ReleasedAt = null;
                    }
                    order.Status = OrderStatusWorkflow.Preparing;
                }
            }
        }
        else if (attempt.Status is PaymentStatusWorkflow.Rejected or PaymentStatusWorkflow.Cancelled or PaymentStatusWorkflow.Expired)
        {
            ReleaseReservations(order);
            order.Status = OrderStatusWorkflow.Cancelled;
        }
        else if (attempt.Status == PaymentStatusWorkflow.Refunded)
        {
            if (order.Status == OrderStatusWorkflow.Preparing)
            {
                foreach (var reservation in order.StockReservations.Where(item => item.Status == "Confirmada"))
                {
                    var product = order.Items.FirstOrDefault(item => item.ProductId == reservation.ProductId)?.Product;
                    if (product is not null) product.Stock += reservation.Quantity;
                    reservation.Status = "Reembolsada";
                }
                order.Status = OrderStatusWorkflow.Cancelled;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public static void ReleaseReservations(Order order)
    {
        foreach (var reservation in order.StockReservations.Where(item => item.Status == "Reservada"))
        {
            reservation.Status = "Liberada";
            reservation.ReleasedAt = DateTime.Now;
        }
    }
}
