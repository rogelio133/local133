namespace Local133.Application.Subscriptions;

/// <summary>
/// Resumen mensual del vendedor («En noviembre cobraste $18,400 en 212 ventas…», §7 del diseño). El job diario de la
/// suscripción lo llama el día 1 con el mes que terminó (5.7); el contenido llega en la 5.10. Debe ser idempotente por
/// mes (llave del aviso) porque el job puede repetirse.
/// </summary>
public interface IMonthlySummaryNotifier
{
    /// <param name="month">Día 1 del mes que terminó (en México).</param>
    Task SendAsync(DateOnly month, CancellationToken cancellationToken = default);
}
