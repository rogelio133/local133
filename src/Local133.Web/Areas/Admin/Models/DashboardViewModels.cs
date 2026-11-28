using System.Globalization;
using Local133.Domain.Merchants;
using Local133.Domain.Subscriptions;

namespace Local133.Web.Areas.Admin.Models;

/// <summary>GMV de un periodo: lo cobrado por los vendedores (suma de pagos) y las ventas distintas que lo cobraron.</summary>
public sealed record GmvFigure(decimal Amount, int Sales);

/// <summary>Ingresos de un plan en un periodo: lo abonado y cuántos cargos.</summary>
public sealed record PlanRevenue(decimal Amount, int Charges);

/// <summary>
/// Ingresos por suscripción de un periodo: cargos abonados (también a mano), con el IVA desglosado y por plan
/// (<see cref="ByPlan"/> trae los dos planes, en ceros si no hubo cargos).
/// </summary>
public sealed record RevenueFigure(
    decimal Amount, decimal Base, decimal Vat, int Charges, int CreditedManually,
    IReadOnlyDictionary<SubscriptionPlan, PlanRevenue> ByPlan)
{
    /// <summary>Los planes en el orden de la pantalla.</summary>
    public static readonly IReadOnlyList<SubscriptionPlan> Plans = [SubscriptionPlan.Basic, SubscriptionPlan.Pro];
}

/// <summary>Un mes de la tabla del tablero.</summary>
public sealed record DashboardMonthRow(DateOnly Month, string Label, GmvFigure Gmv, RevenueFigure Revenue);

/// <summary>Vendedor que pasó el uso justo este mes (Básico o Pro).</summary>
public sealed record FairUseRow(int MerchantId, string Name, string? BusinessName, int PaidCount);

/// <summary>Tablero del admin (/admin, 6.4).</summary>
public sealed class DashboardViewModel
{
    /// <summary>Meses de la tabla (el último es el actual).</summary>
    public const int MonthsShown = 6;

    /// <summary>Ventana de «pruebas que terminaron hace poco».</summary>
    public const int RecentTrialDays = 30;

    public required DateOnly Today { get; init; }

    /// <summary>Cuándo se calculó (el tablero se guarda unos minutos en <c>DashboardCache</c>).</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    public required GmvFigure GmvToday { get; init; }

    /// <summary>Del más viejo al mes actual.</summary>
    public required IReadOnlyList<DashboardMonthRow> Months { get; init; }

    public DashboardMonthRow CurrentMonth => Months[^1];

    public DashboardMonthRow PreviousMonth => Months[^2];

    public required IReadOnlyDictionary<PlanTier, int> ByPlan { get; init; }

    public required IReadOnlyDictionary<MerchantStatus, int> ByStatus { get; init; }

    public int TotalMerchants => ByStatus.Values.Sum();

    public required TrialConversion Conversion { get; init; }

    public required TrialConversion RecentConversion { get; init; }

    /// <summary>
    /// Vendedores que subieron de Básico a Pro en los últimos <see cref="RecentTrialDays"/> días: los que tienen un
    /// periodo <c>Conversion</c> creado en ese lapso (solo lo crea el abono de un Pro con Básico por delante).
    /// </summary>
    public required int RecentUpgrades { get; init; }

    public required int FoundersAssigned { get; init; }

    public required int FounderSlots { get; init; }

    public int FoundersLeft => Math.Max(0, FounderSlots - FoundersAssigned);

    public required int FailedWebhooks { get; init; }

    public required int StuckWebhooks { get; init; }

    public required int FairUseLimit { get; init; }

    public required IReadOnlyList<FairUseRow> FairUse { get; init; }

    /// <summary>Máximos de la tabla, para el ancho de las barras.</summary>
    public decimal MaxMonthGmv => Months.Max(m => m.Gmv.Amount);

    public decimal MaxMonthRevenue => Months.Max(m => m.Revenue.Amount);

    /// <summary>Ancho de una barra en % (0 si el máximo es 0).</summary>
    public static int BarPercent(decimal value, decimal max) =>
        max <= 0 ? 0 : (int)Math.Round(100m * value / max, MidpointRounding.AwayFromZero);

    public static string Count(int value) => value.ToString("N0", DashboardFormats.Mexico);

    public static string Percent(int? value) => value is { } v ? $"{v.ToString(CultureInfo.InvariantCulture)} %" : "—";
}

/// <summary>Formatos del tablero.</summary>
public static class DashboardFormats
{
    public static readonly CultureInfo Mexico = CultureInfo.GetCultureInfo("es-MX");

    /// <summary>«nov 2026».</summary>
    public static string Month(DateOnly month) =>
        month.ToString("MMM yyyy", Mexico).Replace(".", string.Empty, StringComparison.Ordinal);
}
