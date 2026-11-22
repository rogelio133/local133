using Local133.Domain.Common;
using Local133.Domain.Subscriptions;

namespace Local133.Web.Areas.Merchant.Models;

/// <summary>Tarjeta del plan en Inicio (5.3): prueba, Pro, gracia o gratuito. En el onboarding no hay tarjeta.</summary>
public sealed record PlanSummaryViewModel(PlanTier Tier, string Title, string Detail, string Icon)
{
    public static PlanSummaryViewModel? From(PlanStatus status, DateTimeOffset now)
    {
        var today = MexicoTime.Today(now);
        string Date(DateOnly? day) => SaleFormats.Date(day!.Value, today);

        return status.Tier switch
        {
            PlanTier.Onboarding => null,
            PlanTier.Trial => new(status.Tier, TrialTitle(status.DaysLeft(now)!.Value),
                $"Tienes todo Pro hasta el {Date(status.LastProDay)}.", "bi-stars"),
            PlanTier.Pro => new(status.Tier, "Plan Pro", status.LastTrialDay is { } trialDay
                    ? $"Vigente hasta el {Date(status.LastProDay)}. Lo que pagaste empieza al terminar tu prueba, el {Date(trialDay)}."
                    : $"Vigente hasta el {Date(status.LastProDay)}.", "bi-stars"),
            PlanTier.Grace => new(status.Tier, $"Tu plan venció el {Date(status.LastProDay)}",
                $"Tienes hasta el {Date(status.LastGraceDay)} para pagar sin perder Pro.", "bi-hourglass-split"),
            PlanTier.Free => new(status.Tier, "Plan gratuito", "Cobra por monto o por celular sin costo.", "bi-gift"),
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

/// <summary>Banner de gracia (5.7).</summary>
/// <param name="LastGraceDayText">Último día para pagar sin perder Pro («hoy» o «24 de diciembre»).</param>
/// <param name="PayUrl">Página de pago de Pey del link de renovación, o «Mi plan» si no hay link.</param>
/// <param name="IsPaymentLink"><paramref name="PayUrl"/> es el link de Pey.</param>
public sealed record GraceBannerViewModel(string LastProDayText, string LastGraceDayText, string PayUrl, bool IsPaymentLink)
{
    public string DeadlineText => LastGraceDayText == "hoy"
        ? "Hoy es el último día para pagar sin perder Pro."
        : $"Paga a más tardar el {LastGraceDayText} para no perder Pro.";
}
