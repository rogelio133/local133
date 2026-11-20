using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Local133.Infrastructure.Subscriptions;

/// <summary>
/// Prueba de Pro al activarse la cuenta bancaria: una sola vez por vendedor y en el mismo guardado que el cambio de
/// estatus (la agrega al contexto, no guarda). La usan el alta (<c>BankAccountRegistration</c>, si Pey la activa de
/// inmediato) y <c>BankAccountStatusSync</c> (webhook, sondeo y botón). Si dos activaciones llegan a la vez, el
/// índice único de <c>Subscriptions.MerchantId</c> deja pasar solo una; la otra falla al guardar y su reintento ya ve
/// al vendedor activo.
/// </summary>
internal static class TrialStarter
{
    /// <returns>La suscripción nueva, o <c>null</c> si el vendedor ya tenía una (ya tuvo su prueba).</returns>
    public static async Task<Subscription?> EnsureTrialAsync(
        AppDbContext db, int merchantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var exists = await db.Subscriptions.IgnoreQueryFilters().AnyAsync(s => s.MerchantId == merchantId, cancellationToken);
        if (exists)
        {
            return null;
        }

        var trialDays = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Id == SystemSettings.SingletonId)
            .Select(s => s.TrialDays)
            .SingleAsync(cancellationToken);
        var subscription = Subscription.StartTrial(merchantId, now, trialDays);
        db.Subscriptions.Add(subscription);
        return subscription;
    }
}
