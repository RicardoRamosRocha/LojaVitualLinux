using System.Data;
using LojaVirtual.Data;
using LojaVirtual.Models;
using LojaVirtual.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace LojaVirtual.Services;

public interface IOrderCancellationService
{
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);
}

public sealed class OrderCancellationService : IOrderCancellationService
{
    private readonly AppDbContext _context;
    private readonly IMercadoPagoService _mercadoPago;

    public OrderCancellationService(AppDbContext context, IMercadoPagoService mercadoPago)
    {
        _context = context;
        _mercadoPago = mercadoPago;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var order = await _context.Orders
            .Include(item => item.Items)
            .Include(item => item.PaymentAttempts)
            .Include(item => item.StockReservations)
            .SingleOrDefaultAsync(item => item.Id == orderId, cancellationToken)
            ?? throw new OrderCancellationException("order_not_found", "O pedido não foi encontrado.", 404);

        // The terminal state makes retries idempotent and leaves all financial rows intact.
        if (order.Status == OrderStatusWorkflow.Cancelled)
        {
            await transaction.CommitAsync(cancellationToken);
            return order;
        }

        if (order.Status != OrderStatusWorkflow.Pending)
        {
            throw new OrderCancellationException(
                "order_not_cancellable",
                "Este pedido já avançou no fluxo e não pode ser cancelado pelo cliente.",
                409);
        }

        var localBlockingAttempt = order.PaymentAttempts.FirstOrDefault(attempt =>
            attempt.Status is PaymentStatusWorkflow.Approved or
                PaymentStatusWorkflow.Processing or
                PaymentStatusWorkflow.StockReview);
        if (localBlockingAttempt is not null)
        {
            throw new OrderCancellationException(
                "payment_not_cancellable",
                "O pagamento foi aprovado ou está em processamento; o pedido não pode ser cancelado.",
                409);
        }

        IReadOnlyList<MercadoPagoPaymentResult> remotePayments;
        try
        {
            remotePayments = await _mercadoPago.SearchPaymentsByExternalReferenceAsync(
                order.Id.ToString(),
                cancellationToken);
        }
        catch (MercadoPagoException exception)
        {
            throw new OrderCancellationException(
                "payment_verification_unavailable",
                "Não foi possível confirmar o estado do pagamento. Tente novamente.",
                503,
                exception);
        }

        var blockingRemotePayment = remotePayments.FirstOrDefault(payment =>
            payment.Status is "approved" or "in_process" or "authorized");
        if (blockingRemotePayment is not null)
        {
            throw new OrderCancellationException(
                "payment_not_cancellable",
                "O pagamento foi aprovado ou está em processamento; o pedido não pode ser cancelado.",
                409);
        }

        var unknownRemotePayment = remotePayments.FirstOrDefault(payment =>
            payment.Status is not "pending" and
                not "rejected" and
                not "cancelled" and
                not "refunded" and
                not "charged_back" and
                not "expired");
        if (unknownRemotePayment is not null)
        {
            throw new OrderCancellationException(
                "payment_verification_inconclusive",
                "O estado do pagamento não permite confirmar um cancelamento seguro.",
                409);
        }

        try
        {
            foreach (var payment in remotePayments.Where(item => item.Status == "pending"))
            {
                await _mercadoPago.CancelPaymentAsync(payment.PaymentId, cancellationToken);
            }
        }
        catch (MercadoPagoException exception)
        {
            throw new OrderCancellationException(
                "payment_cancellation_unavailable",
                "Não foi possível cancelar o pagamento no Mercado Pago. O pedido permanece aberto.",
                503,
                exception);
        }

        foreach (var attempt in order.PaymentAttempts.Where(item =>
                     item.Status is PaymentStatusWorkflow.CreatingPreference or
                         PaymentStatusWorkflow.AwaitingPayment))
        {
            attempt.Status = PaymentStatusWorkflow.Cancelled;
            attempt.UpdatedAtUtc = DateTime.UtcNow;
        }

        OrderPaymentService.ReleaseReservations(order);
        order.Status = OrderStatusWorkflow.Cancelled;
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return order;
    }
}

public sealed class OrderCancellationException : Exception
{
    public OrderCancellationException(string code, string message, int statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}
