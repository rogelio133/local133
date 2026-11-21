using Local133.Application.Auth;
using Local133.Application.Subscriptions;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Controllers;
using Local133.Web.Areas.Merchant.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Controllers;

/// <summary>
/// Estado de un cargo de la suscripción para la confirmación del pago (polling cada 3 s). Mientras no está pagado
/// consulta a Pey con freno (<see cref="PlanController.StatusCheckMinInterval"/>), así que confirma aunque se pierda el
/// webhook. El filtro multi-tenant hace que el cargo de otro vendedor dé 404; sin sesión responde 401.
/// </summary>
[ApiController]
[Authorize(Roles = Roles.Merchant)]
[Route("api/subscription-charges")]
[ApiExplorerSettings(IgnoreApi = true)]
public class SubscriptionChargesApiController(
    AppDbContext db, ISubscriptionPaymentReconciliation reconciliation, TimeProvider clock) : ControllerBase
{
    /// <param name="ProUntilText">«Tienes Pro hasta el …», solo si ya está pagado.</param>
    public sealed record SubscriptionChargeStatusResponse(int Id, string Status, DateTimeOffset? PaidAt, string? ProUntilText);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SubscriptionChargeStatusResponse>> Get(int id, CancellationToken cancellationToken)
    {
        var charge = await FindAsync(id, cancellationToken);
        if (charge is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound);
        }

        if (charge.Status is not (SubscriptionChargeStatus.Paid or SubscriptionChargeStatus.Failed))
        {
            await reconciliation.ReconcileChargeAsync(charge.Id, PlanController.StatusCheckMinInterval, cancellationToken);
            charge = await FindAsync(id, cancellationToken) ?? charge;
        }

        string? proUntil = null;
        if (charge.Status == SubscriptionChargeStatus.Paid)
        {
            var until = await db.Subscriptions.AsNoTracking()
                .Where(s => s.MerchantId == charge.MerchantId)
                .Select(s => (DateTimeOffset?)s.ProUntil)
                .SingleOrDefaultAsync(cancellationToken);
            proUntil = SubscriptionPaymentViewModel.ProUntilTextFor(until, clock.GetUtcNow());
        }

        Response.Headers.CacheControl = "no-store";
        return new SubscriptionChargeStatusResponse(charge.Id, charge.Status.ToString(), charge.PaidAt, proUntil);
    }

    private Task<ChargeStatus?> FindAsync(int id, CancellationToken cancellationToken) =>
        db.SubscriptionCharges.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new ChargeStatus(c.Id, c.MerchantId, c.Status, c.PaidAt))
            .SingleOrDefaultAsync(cancellationToken);

    private sealed record ChargeStatus(int Id, int MerchantId, SubscriptionChargeStatus Status, DateTimeOffset? PaidAt);
}
