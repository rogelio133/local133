using System.Globalization;
using Local133.Application.Subscriptions;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;

namespace Local133.Web.Areas.Merchant.Models;

/// <summary>Rutas del plan (relativas a la raíz del sitio, para <c>Url.Content("~" + …)</c>).</summary>
public static class PlanUrls
{
    /// <summary>«Mi plan». En la 5.5 es provisional (estado, contador y lo que incluye Pro); el pago llega en la 5.6.</summary>
    public const string Plan = "/app/plan";
}

/// <summary>
/// Aviso «Disponible con Pro» (parcial <c>_ProRequired</c>): qué se gana con Pro y el botón a «Mi plan».
/// </summary>
/// <param name="Detail">Lo que el vendedor ya tiene esperando (p. ej. «Tu catálogo de 63 productos te espera»).</param>
public sealed record ProRequiredViewModel(string Title, string Message, string? Detail = null, string Icon = "bi-stars");

/// <summary>
/// Contador del mes del gratuito (parcial <c>_ChargeUsage</c>): «82 de 100 cobros este mes», aviso al 80 % y bloqueo
/// al llegar al límite. Solo se muestra cuando el plan tiene <see cref="PlanEntitlements.HasMonthlyChargeLimit"/>.
/// </summary>
public sealed record ChargeUsageViewModel(
    ChargeAllowanceState State,
    string Title,
    string Message,
    int Percent,
    string BarClass,
    bool IsBlocked)
{
    public static ChargeUsageViewModel From(MonthlyChargeUsage usage, DateOnly today)
    {
        var a = usage.Allowance;
        var resetsOn = SaleFormats.Date(usage.ResetsOn, today);
        var percent = a.PaidLimit <= 0 ? 100 : Math.Min(100, a.Paid * 100 / a.PaidLimit);
        var title = $"{Count(Math.Min(a.Paid, a.PaidLimit))} de {Count(a.PaidLimit)} cobros este mes";

        return a.State switch
        {
            ChargeAllowanceState.Exhausted => new(a.State, title,
                $"Llegaste al límite del mes. No puedes generar cobros hasta el {resetsOn} o hasta que pagues Pro.",
                100, "bg-danger", IsBlocked: true),
            ChargeAllowanceState.GenerationCapReached => new(a.State, title,
                $"Generaste {Count(a.Generated)} cobros este mes, el máximo del plan gratuito. Podrás generar más el {resetsOn} o con Pro.",
                percent, "bg-danger", IsBlocked: true),
            ChargeAllowanceState.NearLimit => new(a.State, title,
                a.Remaining == 1
                    ? "Te queda 1 cobro este mes. Con Pro cobras sin límite."
                    : $"Te quedan {Count(a.Remaining)} cobros este mes. Con Pro cobras sin límite.",
                percent, "bg-warning", IsBlocked: false),
            _ => new(a.State, title,
                $"El plan gratuito incluye {Count(a.PaidLimit)} cobros al mes. Se reinician el {resetsOn}.",
                percent, "bg-success", IsBlocked: false),
        };
    }

    /// <summary>Hay que llamar la atención (aviso o bloqueo).</summary>
    public bool IsWarning => State is not ChargeAllowanceState.Normal;

    private static string Count(int n) => n.ToString("#,0", CultureInfo.GetCultureInfo("es-MX"));
}

/// <summary>Avisos del bloqueo de cobro por el plan (formulario de Cobrar).</summary>
public static class PlanBlockedNotices
{
    public static ChargeBlockedNotice? For(MonthlyChargeUsage usage, DateOnly today)
    {
        var resetsOn = SaleFormats.Date(usage.ResetsOn, today);
        return usage.Allowance.State switch
        {
            ChargeAllowanceState.Exhausted => new(
                "Llegaste a tus cobros del mes",
                $"Usaste los {usage.Allowance.PaidLimit} cobros del mes del plan gratuito. Podrás cobrar de nuevo el {resetsOn}, o desde hoy con Pro.",
                "~" + PlanUrls.Plan, "Conoce Pro"),
            ChargeAllowanceState.GenerationCapReached => new(
                "Llegaste al máximo de cobros generados",
                $"Generaste {usage.Allowance.Generated} cobros este mes, el máximo del plan gratuito. Podrás cobrar de nuevo el {resetsOn}, o desde hoy con Pro.",
                "~" + PlanUrls.Plan, "Conoce Pro"),
            _ => null,
        };
    }
}

/// <summary>«Mi plan» provisional (5.5): tarjeta del plan, contador del mes y lo que incluye cada plan.</summary>
/// <param name="Plan">Tarjeta del plan; <c>null</c> en el onboarding (la prueba empieza al activarse la cuenta).</param>
public sealed record MyPlanViewModel(
    PlanSummaryViewModel? Plan, ChargeUsageViewModel? Usage, int TrialDays, IReadOnlyList<PlanFeatureRow> Features)
{
    /// <summary>
    /// Lo que incluye cada plan (§2 del diseño), con los números de <see cref="SystemSettings"/>. <c>ComingSoon</c> =
    /// se anuncia, todavía no existe.
    /// </summary>
    public static IReadOnlyList<PlanFeatureRow> FeaturesFor(SystemSettings settings) =>
    [
        new("Cobro por monto con QR o al celular", "Sí", "Sí"),
        new("Cobros concretados al mes", $"{settings.FreeMonthlyPaidLimit:N0}", "Sin límite"),
        new("Catálogo de productos", "Solo lectura", "Sí"),
        new("Cobro con productos (carrito)", "—", "Sí"),
        new("Recibo", "Liga para copiar", "WhatsApp y compartir"),
        new("Historial de ventas", $"Últimos {settings.FreeHistoryDays:N0} días", "Completo, con fechas"),
        new("Vendedores adicionales", "—", "3", ComingSoon: true),
        new("QR impreso del puesto", "—", "Sí", ComingSoon: true),
        new("Reportes", "—", "Sí", ComingSoon: true),
    ];
}

public sealed record PlanFeatureRow(string Feature, string Free, string Pro, bool ComingSoon = false);
