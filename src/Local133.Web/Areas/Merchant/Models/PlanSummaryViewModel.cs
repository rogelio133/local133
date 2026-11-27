using Local133.Domain.Common;
using Local133.Domain.Subscriptions;

namespace Local133.Web.Areas.Merchant.Models;

/// <summary>
/// Tarjeta del plan en Inicio y en «Mi plan» (5.3, 5.12): prueba, Básico, Pro, gracia o vencido, con lo que viene
/// después («Pro hasta el 9 de diciembre, después Básico hasta el 9 de enero»). En el onboarding no hay tarjeta.
/// </summary>
public sealed record PlanSummaryViewModel(PlanTier Tier, string Title, string Detail, string Icon)
{
    /// <summary>Color del ícono.</summary>
    public string IconClass => Tier switch
    {
        PlanTier.Grace => "text-warning",
        PlanTier.Expired => "text-danger",
        _ => "text-primary",
    };

    /// <summary>Borde de la tarjeta: de aviso en la gracia y en el vencido.</summary>
    public string BorderClass => Tier switch
    {
        PlanTier.Grace => "border-warning",
        PlanTier.Expired => "border-danger",
        _ => string.Empty,
    };

    public static PlanSummaryViewModel? From(PlanStatus status, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(status);
        var today = MexicoTime.Today(now);
        string Date(DateOnly? day) => SaleFormats.Date(day!.Value, today);
        var lastPlanDay = status.LastPlanDay ?? status.LastActiveDay;
        string? Next() => status.NextPlan is { } next && status.NextPlanLastDay is { } nextLast
            ? $"{status.Plan!.Value.Label()} hasta el {Date(lastPlanDay)}, después {next.Label()} hasta el {Date(nextLast)}."
            : null;

        return status.Tier switch
        {
            PlanTier.Onboarding => null,
            PlanTier.Trial => new(status.Tier, TrialTitle(status.DaysLeft(now)!.Value),
                status.NextPlan is { } next && status.NextPlanStartDay is { } nextStart
                    ? $"Tienes todo Pro hasta el {Date(lastPlanDay)}. Tu plan {next.Label()} empieza el {Date(nextStart)}."
                    : $"Tienes todo Pro hasta el {Date(lastPlanDay)}.", "bi-stars"),
            PlanTier.Pro => new(status.Tier, "Plan Pro", Next() ?? (status.LastTrialDay is { } trialDay
                    ? $"Vigente hasta el {Date(lastPlanDay)}. Lo que pagaste empieza al terminar tu prueba, el {Date(trialDay)}."
                    : $"Vigente hasta el {Date(lastPlanDay)}."), "bi-stars"),
            PlanTier.Basic => new(status.Tier, "Plan Básico", Next() ?? $"Vigente hasta el {Date(lastPlanDay)}.", "bi-shop"),
            PlanTier.Grace => new(status.Tier, $"Tu plan venció el {Date(status.LastActiveDay)}",
                $"Tienes hasta el {Date(status.LastGraceDay)} para pagar sin perder {status.Plan!.Value.Label()}.",
                "bi-hourglass-split"),
            PlanTier.Expired => new(status.Tier, $"Tu plan venció el {Date(status.LastActiveDay)}",
                "Elige un plan para seguir cobrando.", "bi-x-circle"),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status.Tier, "Estado del plan desconocido."),
        };
    }

    /// <summary>«Prueba: te quedan 23 días», «te queda 1 día» o «tu prueba termina hoy».</summary>
    public static string TrialTitle(int daysLeft) => daysLeft switch
    {
        <= 0 => "Prueba: termina hoy",
        1 => "Prueba: te queda 1 día",
        _ => $"Prueba: te quedan {daysLeft} días",
    };
}

/// <summary>Banner de gracia (5.7). Su botón siempre va a «Mi plan»: ahí se elige el paquete y nace el link de pago.</summary>
/// <param name="PlanLabel">Plan que pagó: «Básico» o «Pro».</param>
/// <param name="LastGraceDayText">Último día para pagar sin perder el plan («hoy» o «24 de diciembre»).</param>
public sealed record GraceBannerViewModel(string PlanLabel, string LastActiveDayText, string LastGraceDayText)
{
    public string DeadlineText => LastGraceDayText == "hoy"
        ? $"Hoy es el último día para pagar sin perder {PlanLabel}."
        : $"Paga a más tardar el {LastGraceDayText} para no perder {PlanLabel}.";
}
