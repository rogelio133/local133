using Local133.Application.Auth;
using Local133.Domain.Common;
using Local133.Domain.Merchants;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Models;
using Local133.Web.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Local133.Web.Areas.Merchant.Controllers;

/// <summary>
/// «Invita y gana» (/app/invita, 5.8): el código y la liga del vendedor para compartir, los premios ganados y sus
/// referidos. Para todos los planes (también el vencido y el onboarding); solo lectura. Los referidos salen del filtro
/// multi-tenant (los del vendedor de la sesión).
/// </summary>
[Area("Merchant")]
[Authorize(Roles = Roles.Merchant)]
[Route("app/invita")]
[ApiExplorerSettings(IgnoreApi = true)]
public class InviteController(AppDbContext db, IOptions<PublicSiteOptions> site, TimeProvider clock) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        var settings = await db.SystemSettings.AsNoTracking().SingleAsync(s => s.Id == SystemSettings.SingletonId, cancellationToken);
        var now = clock.GetUtcNow();
        var today = MexicoTime.Today(now);

        var referrals = await db.Referrals.AsNoTracking()
            .Where(r => r.ReferrerMerchantId == merchant.Id)
            .OrderByDescending(r => r.RegisteredAt).ThenByDescending(r => r.Id)
            .Take(InviteViewModel.MaxReferrals)
            .ToListAsync(cancellationToken);

        var referredIds = referrals.Select(r => r.ReferredMerchantId).ToList();
        var names = await db.Merchants.AsNoTracking()
            .Where(m => referredIds.Contains(m.Id))
            .Select(m => new { m.Id, m.OwnerName, m.BusinessName })
            .ToDictionaryAsync(m => m.Id, m => Domain.Merchants.Merchant.GreetingNameFor(m.OwnerName, m.BusinessName), cancellationToken);

        var rewardPeriods = await db.SubscriptionPeriods.AsNoTracking()
            .Where(p => p.MerchantId == merchant.Id && p.Kind == SubscriptionPeriodKind.ReferralReward && p.ReferralId != null)
            .Select(p => new { p.ReferralId, p.StartsAt, p.EndsAt })
            .ToListAsync(cancellationToken);
        var daysByReferral = rewardPeriods.ToDictionary(
            p => p.ReferralId!.Value, p => (int)Math.Round((p.EndsAt - p.StartsAt).TotalDays));

        var windowStart = ReferralRewardPolicy.WindowStart(now);
        var rewardsInWindow = await db.Referrals.AsNoTracking()
            .CountAsync(r => r.ReferrerMerchantId == merchant.Id && r.RewardedAt >= windowStart && r.RevokedAt == null, cancellationToken);

        var url = site.Value.BaseUrlFor(Request) + ReferralCode.PathFor(merchant.ReferralCode);
        return View(InviteViewModel.Create(
            merchant.ReferralCode,
            url,
            settings,
            rewardsInWindow,
            daysByReferral.Values.Sum(),
            merchant.Status == MerchantStatus.Disabled,
            // Sin suscripción: su prueba (y con ella sus premios) empieza con su primer cobro (5.13).
            !await db.Subscriptions.AnyAsync(s => s.MerchantId == merchant.Id, cancellationToken),
            referrals.Select(r => ReferralRow.From(
                r,
                names.GetValueOrDefault(r.ReferredMerchantId),
                daysByReferral.TryGetValue(r.Id, out var days) ? days : null,
                today,
                settings.ReferralRewardsPerYear)).ToList()));
    }
}
