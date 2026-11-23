using Local133.Application.Subscriptions;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.Subscriptions;

/// <summary>
/// Resumen mensual provisional: solo deja el log. El job diario ya lo llama el día 1 (5.7); la 5.10 lo reemplaza por
/// el aviso y el correo con lo que cobró el vendedor.
/// </summary>
internal sealed class LoggingMonthlySummaryNotifier(ILogger<LoggingMonthlySummaryNotifier> logger) : IMonthlySummaryNotifier
{
    public Task SendAsync(DateOnly month, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Resumen mensual de {Month:yyyy-MM}: su contenido llega en la 5.10", month);
        return Task.CompletedTask;
    }
}
