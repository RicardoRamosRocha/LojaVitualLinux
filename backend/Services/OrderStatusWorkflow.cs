namespace LojaVirtual.Services;

public static class OrderStatusWorkflow
{
    public const string Pending = "Pendente";
    public const string Preparing = "Em preparação";
    public const string Shipped = "Enviado";
    public const string Delivered = "Entregue";
    public const string Cancelled = "Cancelado";

    public static bool TryGetNextStatus(string currentStatus, out string? nextStatus)
    {
        nextStatus = currentStatus switch
        {
            Pending => Preparing,
            Preparing => Shipped,
            Shipped => Delivered,
            _ => null
        };

        return nextStatus is not null;
    }

    public static bool IsAllowedTransition(string currentStatus, string requestedStatus)
    {
        return TryGetNextStatus(currentStatus, out var nextStatus)
            && string.Equals(nextStatus, requestedStatus, StringComparison.Ordinal);
    }
}
