using System.Globalization;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using Local133.Web.Models;

namespace Local133.Web.Areas.Merchant.Models;

/// <summary>«Invita y gana» (/app/invita, 5.8): código, liga para compartir, premios y referidos.</summary>
/// <param name="Url">Liga absoluta de invitación (<c>/r/{código}</c>).</param>
/// <param name="Message">Texto para WhatsApp y «Compartir» (incluye la liga).</param>
/// <param name="RewardsInWindow">Premios ganados en los últimos 12 meses (para el tope).</param>
/// <param name="DaysEarned">Días de Pro ganados en total con referidos.</param>
/// <param name="IsDisabled">Cuenta dada de baja: ya no gana premios.</param>
/// <param name="IsOnboarding">Aún no tiene suscripción (no ha cobrado): los premios esperan a su prueba.</param>
public sealed record InviteViewModel(
    string Code,
    string Url,
    string Message,
    string WhatsAppUrl,
    string WhatsAppAppUrl,
    string WhatsAppWebUrl,
    string DiscountText,
    int RewardDays,
    int RewardsPerYear,
    int RewardsInWindow,
    int DaysEarned,
    bool IsDisabled,
    bool IsOnboarding,
    IReadOnlyList<ReferralRow> Referrals)
{
    /// <summary>Referidos que se muestran (los más recientes).</summary>
    public const int MaxReferrals = 50;

    private static readonly CultureInfo Mexico = CultureInfo.GetCultureInfo("es-MX");

    public int PendingCount => Referrals.Count(r => r.State == ReferralRowState.NotPaid);

    /// <summary>
    /// «30 % de un mes»: lo que se ahorra el referido en su primer pago. Sin monto, porque depende de su precio (normal
    /// o de fundador); «Mi plan» del referido le muestra el monto exacto.
    /// </summary>
    public static string DiscountTextFor(SystemSettings settings) =>
        $"{PercentText(settings.ReferralWelcomeDiscountRate)} de un mes";

    /// <summary>«30 %»: la tasa del descuento de bienvenida.</summary>
    public static string PercentText(decimal rate) => (rate * 100).ToString("0.##", Mexico) + " %";

    /// <summary>Mensaje para compartir: invita, da el código, el descuento y la liga, en tres renglones.</summary>
    public static string MessageFor(string code, string url, SystemSettings settings) =>
        $"Cobra con CoDi en tu negocio con Local133.\nRegístrate con mi código {code} y llévate {PercentText(settings.ReferralWelcomeDiscountRate)} de descuento en tu primer pago de Pro:\n{url}";

    public static InviteViewModel Create(
        string code, string url, SystemSettings settings, int rewardsInWindow, int daysEarned, bool isDisabled,
        bool isOnboarding, IReadOnlyList<ReferralRow> referrals)
    {
        var message = MessageFor(code, url, settings);
        return new InviteViewModel(
            code,
            url,
            message,
            ReceiptLinks.WhatsAppUrl(message),
            ReceiptLinks.WhatsAppAppUrl(message),
            ReceiptLinks.WhatsAppWebUrl(message),
            DiscountTextFor(settings),
            settings.ReferralRewardDays,
            settings.ReferralRewardsPerYear,
            rewardsInWindow,
            daysEarned,
            isDisabled,
            isOnboarding,
            referrals);
    }
}

public enum ReferralRowState
{
    NotPaid,
    Rewarded,
    PendingReward,
    Skipped,
}

/// <summary>Un referido en «Invita y gana»: solo su primer nombre (o el de su negocio), nunca su correo.</summary>
public sealed record ReferralRow(string Name, string RegisteredText, ReferralRowState State, string StatusText)
{
    public string BadgeClass => State switch
    {
        ReferralRowState.Rewarded => "text-bg-success",
        ReferralRowState.PendingReward => "text-bg-info",
        ReferralRowState.Skipped => "text-bg-warning",
        _ => "text-bg-light border",
    };

    public static ReferralRow From(Referral referral, string? name, int? daysEarned, DateOnly today, int rewardsPerYear)
    {
        var (state, text) = referral switch
        {
            { QualifiedAt: null } => (ReferralRowState.NotPaid, "Aún no paga"),
            { RevokedAt: not null } => (ReferralRowState.Skipped, "Pagó · premio anulado"),
            { RewardedAt: not null } => (ReferralRowState.Rewarded,
                daysEarned is { } d ? $"Pagó · ganaste {DaysText(d)}" : "Pagó · ganaste días de Pro"),
            { SkippedReason: ReferralSkipReason.RewardCap } => (ReferralRowState.Skipped,
                $"Pagó · sin premio (tope de {rewardsPerYear.ToString(CultureInfo.InvariantCulture)} al año)"),
            { SkippedReason: not null } => (ReferralRowState.Skipped, "Pagó · sin premio"),
            _ => (ReferralRowState.PendingReward, "Pagó · tus días llegan al activar tu cuenta"),
        };
        return new ReferralRow(
            string.IsNullOrWhiteSpace(name) ? "Vendedor" : name.Trim(),
            $"Se registró el {SaleFormats.Date(Domain.Common.MexicoTime.DateOf(referral.RegisteredAt), today)}",
            state,
            text);
    }

    public static string DaysText(int days) => days == 1 ? "1 día" : $"{days.ToString(CultureInfo.InvariantCulture)} días";
}
