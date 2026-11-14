using Local133.Application.Sales;
using Local133.Domain.Sales;
using Local133.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.Sales;

/// <summary>
/// Ventas pendientes con el QR vencido ⇒ <see cref="SaleStatus.Expired"/>, con un <c>UPDATE</c> condicional (atómico):
/// si el webhook ya la marcó pagada no coincide, y si el job gana la carrera el pago la deja pagada igual
/// (<see cref="Sale.MarkPaid"/>).
/// </summary>
internal sealed class SaleExpiration(AppDbContext db, TimeProvider clock, ILogger<SaleExpiration> logger) : ISaleExpiration
{
    public async Task<int> ExpireDueAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var expired = await db.Sales.IgnoreQueryFilters()
            .Where(s => s.Status == SaleStatus.Pending && s.ExpiresAt != null && s.ExpiresAt <= now)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.Status, SaleStatus.Expired), cancellationToken);

        if (expired > 0)
        {
            logger.LogInformation("{Count} ventas pendientes expiraron", expired);
        }

        return expired;
    }
}
