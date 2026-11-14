namespace Local133.Application.Sales;

/// <summary>
/// Aviso en tiempo real al vendedor (en el sitio: SignalR, grupo por vendedor, evento <see cref="PaymentReceivedEvent"/>).
/// Fuera del sitio (jobs, <c>dotnet ef</c>) no hace nada. Se llama después de guardar.
/// </summary>
public interface ISaleNotifier
{
    public const string PaymentReceivedEvent = "PaymentReceived";

    Task PaymentReceivedAsync(int merchantId, int saleId, CancellationToken cancellationToken = default);
}

/// <summary>Pasa a <c>Expired</c> las ventas pendientes con el QR vencido (job cada minuto).</summary>
public interface ISaleExpiration
{
    /// <returns>Ventas que expiraron.</returns>
    Task<int> ExpireDueAsync(CancellationToken cancellationToken = default);
}

/// <summary>El vendedor (por su usuario de Identity) cancela un cobro pendiente.</summary>
public sealed record CancelSaleCommand(string UserId, int SaleId);

public abstract record CancelSaleResult
{
    private CancelSaleResult()
    {
    }

    public sealed record Cancelled : CancelSaleResult;

    /// <summary>No existe o es de otro vendedor.</summary>
    public sealed record NotFound : CancelSaleResult;

    /// <summary>Ya no está pendiente (pagada, vencida o cancelada); <see cref="Message"/> se muestra al vendedor.</summary>
    public sealed record NotCancellable(string Message) : CancelSaleResult;
}
