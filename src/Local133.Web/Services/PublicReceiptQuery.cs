using Local133.Application.Merchants;
using Local133.Application.Subscriptions;
using Local133.Domain.Common;
using Local133.Domain.Sales;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Models;
using Local133.Web.PublicPages;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Services;

/// <summary>
/// Recibo público de una venta pagada (2.8, marca 5.10): lo dibuja la portada (<c>/recibo/{token}</c>) con los datos de
/// <c>GET /api/public/recibos/{token}</c>. Sin filtro multi-tenant: el token es la única llave.
/// <para>
/// Marca, según el plan del vendedor <b>al abrir la liga</b>: con Pro lleva el logo del negocio (servido por el portal en
/// <c>/recibo/{token}/logo</c>); en Básico y vencido, el código de invitación para «Cobrado con Local133».
/// </para>
/// </summary>
public static class PublicReceiptQuery
{
    /// <summary>La venta con recibo de ese token (sin filtro multi-tenant), o <c>null</c>.</summary>
    public static async Task<Sale?> FindSaleAsync(
        AppDbContext db, string token, bool includeItems, CancellationToken cancellationToken)
    {
        if (!Sale.IsValidReceiptToken(token))
        {
            return null;
        }

        var query = db.Sales.AsNoTracking().IgnoreQueryFilters();
        if (includeItems)
        {
            query = query.Include(s => s.Items);
        }

        var sale = await query.SingleOrDefaultAsync(s => s.ReceiptToken == token, cancellationToken);
        return sale is { HasReceipt: true } ? sale : null;
    }

    /// <summary>Ruta del logo del recibo en el portal, con la versión de la imagen.</summary>
    public static string LogoPathFor(string token, string logoKey) =>
        $"/recibo/{token}/logo?v={MerchantImageRules.Version(logoKey)}";

    /// <param name="sale">Venta con recibo y con sus productos (<see cref="FindSaleAsync"/> con <c>includeItems</c>).</param>
    /// <param name="baseUrl">Base absoluta del portal (sin diagonal final) para la URL del logo.</param>
    public static async Task<PublicReceipt> BuildAsync(
        AppDbContext db, IPlanAccess plans, Sale sale, string baseUrl, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var merchant = await db.Merchants.AsNoTracking()
            .Where(m => m.Id == sale.MerchantId)
            .Select(m => new { m.BusinessName, m.LogoKey, m.ReferralCode })
            .SingleAsync(cancellationToken);
        var branded = (await plans.GetForMerchantAsync(sale.MerchantId, cancellationToken)).Entitlements.HasBrandedReceipt;
        var payments = await db.Payments.AsNoTracking().IgnoreQueryFilters()
            .Where(p => p.SaleId == sale.Id)
            .OrderBy(p => p.PaidAt).ThenBy(p => p.Id)
            .Select(p => new { p.PaidAt, p.Amount, p.TrackingKey })
            .ToListAsync(cancellationToken);

        var today = MexicoTime.Today(now);
        var paidTotal = payments.Sum(p => p.Amount);
        return new PublicReceipt(
            string.IsNullOrWhiteSpace(merchant.BusinessName) ? null : merchant.BusinessName.Trim(),
            SaleQrViewModel.FormatAmount(paidTotal > 0 ? paidTotal : sale.Amount),
            Sale.ConceptTextOf(sale.Concept),
            sale.PaidAt is { } paidAt ? SaleFormats.DateTime(paidAt, today) : null,
            sale.Items.OrderBy(i => i.Id)
                .Select(i => new PublicReceiptItem(
                    i.Name, i.Quantity, SaleQrViewModel.FormatAmount(i.UnitPrice), SaleQrViewModel.FormatAmount(i.Amount)))
                .ToList(),
            payments
                .Select(p => new PublicReceiptPayment(
                    SaleFormats.DateTime(p.PaidAt, today), SaleQrViewModel.FormatAmount(p.Amount), p.TrackingKey))
                .ToList(),
            branded && merchant.LogoKey is { } logoKey ? baseUrl + LogoPathFor(sale.ReceiptToken, logoKey) : null,
            branded ? null : merchant.ReferralCode);
    }
}
