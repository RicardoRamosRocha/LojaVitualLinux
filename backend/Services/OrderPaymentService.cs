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
        var expirationMinutes = _configuration.GetValue("MercadoPago:PreferenceExpirationMinutes", 30);
        OrderPaymentAttempt attempt;

        await using (var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken))
        {
            var current = await _context.Orders
                .Include(item => item.Items)
                .ThenInclude(item => item.Product)
                .Include(item => item.StockReservations)
                .Include(item => item.PaymentAttempts)
                .SingleOrDefaultAsync(item => item.Id == order.Id, cancellationToken)
                ?? throw new PaymentEligibilityException("order_not_found", "O pedido não foi encontrado.");

            await EnsureActiveReservationAsync(current, cancellationToken);
            var reservationExpiresAtUtc = GetReservationExpiresAtUtc(current);

            attempt = current.PaymentAttempts
                .OrderByDescending(item => item.AttemptNumber)
                .FirstOrDefault(item => item.Status is not PaymentStatusWorkflow.Rejected and
                                        not PaymentStatusWorkflow.Cancelled and
                                        not PaymentStatusWorkflow.Expired &&
                                        !string.IsNullOrWhiteSpace(item.CheckoutUrl))
                ?? current.PaymentAttempts.FirstOrDefault(item =>
                    item.Status == PaymentStatusWorkflow.CreatingPreference &&
                    string.IsNullOrWhiteSpace(item.CheckoutUrl))
                ?? CreatePreferenceAttempt(current, expirationMinutes);

            if (string.IsNullOrWhiteSpace(attempt.CheckoutUrl))
            {
                attempt.ExpiresAtUtc = reservationExpiresAtUtc.UtcDateTime;
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            order = current;
        }

        if (!string.IsNullOrWhiteSpace(attempt.CheckoutUrl)) return attempt;

        // The intent and its stable Mercado Pago idempotency key are persisted
        // before the external call. A retry after a crash therefore reuses the
        // same key and lets Mercado Pago deduplicate the preference creation.
        var preference = await _mercadoPago.CreatePreferenceAsync(
            order,
            attempt.IdempotencyKey,
            cancellationToken);

        attempt.PreferenceId = preference.PreferenceId;
        attempt.CheckoutUrl = preference.InitPoint;
        attempt.Status = PaymentStatusWorkflow.AwaitingPayment;
        attempt.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return attempt;
    }

    private async Task EnsureActiveReservationAsync(Order order, CancellationToken cancellationToken)
    {
        if (!string.Equals(order.Status, OrderStatusWorkflow.Pending, StringComparison.Ordinal))
        {
            throw new PaymentEligibilityException(
                "order_not_pending",
                "O pedido não está pendente e não pode iniciar um pagamento.");
        }

        var now = DateTime.Now;
        var quantities = order.Items
            .GroupBy(item => item.ProductId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));
        var productIds = quantities.Keys.ToArray();
        var products = await _context.Products
            .Where(product => productIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);
        var reservedByOtherOrders = await _context.StockReservations
            .Where(reservation => reservation.OrderId != order.Id &&
                                  reservation.Status == "Reservada" &&
                                  reservation.ExpiresAt > now &&
                                  productIds.Contains(reservation.ProductId))
            .GroupBy(reservation => reservation.ProductId)
            .Select(group => new { ProductId = group.Key, Quantity = group.Sum(item => item.Quantity) })
            .ToDictionaryAsync(item => item.ProductId, item => item.Quantity, cancellationToken);

        foreach (var (productId, quantity) in quantities)
        {
            if (!products.TryGetValue(productId, out var product) || !product.IsActive)
            {
                throw new PaymentEligibilityException(
                    "product_not_available",
                    "Um produto do pedido não está mais disponível.");
            }

            var available = product.Stock - reservedByOtherOrders.GetValueOrDefault(productId);
            if (quantity < 1 || quantity > available)
            {
                throw new PaymentEligibilityException(
                    "insufficient_stock",
                    "O estoque disponível não é suficiente para retomar o pagamento.");
            }
        }

        var expiresAt = now.AddMinutes(30);
        foreach (var (productId, quantity) in quantities)
        {
            var reservation = order.StockReservations
                .FirstOrDefault(item => item.ProductId == productId && item.Status == "Reservada");
            if (reservation is null)
            {
                reservation = new OrderStockReservation
                {
                    OrderId = order.Id,
                    ProductId = productId,
                    Quantity = quantity,
                    Status = "Reservada",
                    CreatedAt = now
                };
                order.StockReservations.Add(reservation);
            }

            reservation.Quantity = quantity;
            reservation.Status = "Reservada";
            reservation.ExpiresAt = expiresAt;
            reservation.ReleasedAt = null;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static OrderPaymentAttempt CreatePreferenceAttempt(Order order, int expirationMinutes)
    {
        var attemptNumber = order.PaymentAttempts
            .Select(item => item.AttemptNumber)
            .DefaultIfEmpty(0)
            .Max() + 1;
        var attempt = new OrderPaymentAttempt
        {
            OrderId = order.Id,
            AttemptNumber = attemptNumber,
            Status = PaymentStatusWorkflow.CreatingPreference,
            Amount = order.Total,
            Currency = "BRL",
            IdempotencyKey = $"angel-order-{order.Id}-attempt-{attemptNumber}",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(expirationMinutes)
        };
        order.PaymentAttempts.Add(attempt);
        return attempt;
    }

    private static DateTimeOffset GetReservationExpiresAtUtc(Order order)
    {
        var expiresAt = order.StockReservations
            .Where(item => item.Status == "Reservada")
            .Select(item => item.ExpiresAt)
            .Min();
        return new DateTimeOffset(DateTime.SpecifyKind(expiresAt, DateTimeKind.Local)).ToUniversalTime();
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

public sealed class PaymentEligibilityException : Exception
{
    public PaymentEligibilityException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
