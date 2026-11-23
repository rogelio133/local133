using Local133.Application.Subscriptions;
using Local133.Domain.BankAccounts;
using Local133.Domain.Common;
using Local133.Domain.Sales;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Admin.Models;
using Local133.Web.Areas.Merchant.Models;
using Local133.Web.Areas.Merchant.Services;
using Local133.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Areas.Admin.Services;

/// <summary>
/// Pestañas del detalle de un cliente en el admin. Solo lectura (las acciones llegan en la 6.2 y la 6.3). Devuelven
/// <c>null</c> si el vendedor no existe. Nunca muestran la CLABE completa ni el celular completo.
/// </summary>
public sealed class CustomerDetailQuery(AppDbContext db, IPlanAccess plans, IChargeUsage usage, TimeProvider clock)
{
    public async Task<CustomerGeneralViewModel?> GeneralAsync(int id, CancellationToken cancellationToken)
    {
        var data = await (
            from m in db.Merchants.AsNoTracking()
            join u in db.Users on m.UserId equals u.Id
            where m.Id == id
            select new { Merchant = m, u.Email, u.EmailConfirmed, u.TermsAcceptedAt, u.TermsVersion, UserId = u.Id })
            .SingleOrDefaultAsync(cancellationToken);
        if (data is null)
        {
            return null;
        }

        var header = await HeaderAsync(data.Merchant, data.Email, CustomerTab.General, cancellationToken);
        var logins = await db.UserLogins.AsNoTracking()
            .Where(l => l.UserId == data.UserId)
            .Select(l => l.ProviderDisplayName ?? l.LoginProvider)
            .OrderBy(name => name)
            .ToListAsync(cancellationToken);
        var today = Today;
        return new CustomerGeneralViewModel(
            header,
            data.Merchant.OwnerName,
            data.Merchant.BusinessName,
            data.Email ?? string.Empty,
            data.EmailConfirmed,
            logins,
            SaleFormats.DateTime(data.Merchant.CreatedAt, today),
            data.TermsAcceptedAt is { } accepted
                ? $"{SaleFormats.DateTime(accepted, today)}{(data.TermsVersion is { } v ? $" (versión {v})" : null)}"
                : null,
            data.Merchant.ActivationEmailSentAt is { } sent ? SaleFormats.DateTime(sent, today) : null);
    }

    public async Task<CustomerAccountViewModel?> AccountAsync(int id, CancellationToken cancellationToken)
    {
        var header = await HeaderAsync(id, CustomerTab.Account, cancellationToken);
        if (header is null)
        {
            return null;
        }

        var account = await db.BankAccounts.AsNoTracking().SingleOrDefaultAsync(b => b.MerchantId == id, cancellationToken);
        var today = Today;
        return new CustomerAccountViewModel(
            header,
            account is null
                ? null
                : new CustomerBankAccountView(
                    account.HolderName,
                    Clabe.Mask(account.Clabe),
                    account.Alias,
                    account.Entity,
                    account.PeyBankAccountId,
                    account.CodiStatus,
                    account.ErrorMessage,
                    SaleFormats.DateTime(account.SubmittedAt, today),
                    SaleFormats.DateTime(account.StatusUpdatedAt, today),
                    account.StatusCheckedAt is { } checkedAt ? SaleFormats.DateTime(checkedAt, today) : null));
    }

    public async Task<CustomerSalesViewModel?> SalesAsync(
        int id, SaleStatusFilter status, int? page, CancellationToken cancellationToken)
    {
        var header = await HeaderAsync(id, CustomerTab.Sales, cancellationToken);
        if (header is null)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        var today = MexicoTime.Today(now);
        var businessName = await db.Merchants.Where(m => m.Id == id).Select(m => m.BusinessName).SingleAsync(cancellationToken);
        var payments = db.Payments.AsNoTracking().Where(p => p.MerchantId == id);
        var (monthStart, monthEnd) = MexicoTime.MonthRange(today);
        var collectedTotal = await payments.SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
        var collectedMonth = await payments.Where(p => p.PaidAt >= monthStart && p.PaidAt < monthEnd)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

        var all = db.Sales.AsNoTracking().Where(s => s.MerchantId == id);
        var paidSales = await all.CountAsync(s => s.Status == SaleStatus.Paid, cancellationToken);
        var sales = SalesHistoryQuery.WithStatus(all, status, now);
        var pageInfo = PageInfo.For(page, CustomerSalesViewModel.PageSize, await sales.CountAsync(cancellationToken));
        var rows = await sales
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .Skip(pageInfo.Skip).Take(pageInfo.PageSize)
            .Select(s => new { s.Id, s.Folio, s.CreatedAt, s.Concept, s.Amount, s.Status, s.ExpiresAt, s.Method, s.Source, s.PhoneNumber })
            .ToListAsync(cancellationToken);

        var month = await usage.GetMonthAsync(id, cancellationToken);
        var plan = await plans.GetForMerchantAsync(id, cancellationToken);
        return new CustomerSalesViewModel
        {
            Header = header,
            CollectedTotalText = SaleQrViewModel.FormatAmount(collectedTotal),
            CollectedMonthText = SaleQrViewModel.FormatAmount(collectedMonth),
            PaidSalesCount = paidSales,
            MonthPaidCount = month.Paid,
            MonthGeneratedCount = month.Generated,
            MonthlyLimit = plan.Entitlements.HasMonthlyChargeLimit ? month.Allowance.PaidLimit : null,
            Rows = rows.Select(s => new CustomerSaleRow(
                    s.Id,
                    s.Folio,
                    SaleFormats.DateTime(s.CreatedAt, today),
                    Sale.ConceptTextOf(s.Concept),
                    SaleQrViewModel.FormatAmount(s.Amount),
                    s.Status == SaleStatus.Pending && s.ExpiresAt is { } expiresAt && now >= expiresAt ? SaleStatus.Expired : s.Status,
                    s.Method,
                    s.Source,
                    s.PhoneNumber is null ? null : PhoneNumber.Mask(s.PhoneNumber)))
                .ToList(),
            Page = pageInfo,
            Status = status,
        };
    }

    public async Task<CustomerPlanViewModel?> PlanAsync(int id, CancellationToken cancellationToken)
    {
        var header = await HeaderAsync(id, CustomerTab.Plan, cancellationToken);
        if (header is null)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        var today = MexicoTime.Today(now);
        var status = (await plans.GetForMerchantAsync(id, cancellationToken)).Status;
        var subscription = await db.Subscriptions.AsNoTracking().SingleOrDefaultAsync(s => s.MerchantId == id, cancellationToken);
        return new CustomerPlanViewModel(
            header,
            status,
            AdminLabels.PlanDate(status, today),
            status.DaysLeft(now),
            subscription is null ? null : SaleFormats.DateTime(subscription.TrialStartedAt, today),
            status.LastProDay is { } lastPro ? SaleFormats.Date(lastPro, today) : null,
            status.LastGraceDay is { } lastGrace ? SaleFormats.Date(lastGrace, today) : null,
            subscription?.IsFounder ?? false,
            subscription?.FounderNumber,
            subscription?.LastPackage,
            subscription?.LockedAnnualPrice is { } locked ? SaleQrViewModel.FormatAmount(locked) : null);
    }

    private DateOnly Today => MexicoTime.Today(clock.GetUtcNow());

    private async Task<CustomerHeaderViewModel?> HeaderAsync(int id, CustomerTab tab, CancellationToken cancellationToken)
    {
        var data = await (
            from m in db.Merchants.AsNoTracking()
            join u in db.Users on m.UserId equals u.Id
            where m.Id == id
            select new { Merchant = m, u.Email })
            .SingleOrDefaultAsync(cancellationToken);
        return data is null ? null : await HeaderAsync(data.Merchant, data.Email, tab, cancellationToken);
    }

    private async Task<CustomerHeaderViewModel> HeaderAsync(
        Domain.Merchants.Merchant merchant, string? email, CustomerTab tab, CancellationToken cancellationToken)
    {
        var plan = await plans.GetForMerchantAsync(merchant.Id, cancellationToken);
        var (name, business) = CustomerListQuery.NamesOf(merchant.OwnerName, merchant.BusinessName, email);
        return new CustomerHeaderViewModel(merchant.Id, name, business, email ?? string.Empty, merchant.Status, plan.Tier, tab);
    }
}
