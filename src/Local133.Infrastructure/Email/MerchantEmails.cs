using System.Globalization;
using Local133.Domain.Subscriptions;

namespace Local133.Infrastructure.Email;

/// <summary>Correos de avisos al vendedor que salen de webhooks y jobs (sin petición HTTP).</summary>
public static class MerchantEmails
{
    public const string SubscriptionPaidSubject = "Recibimos tu pago de Pro · Local133";

    /// <summary>Ruta del botón del correo de pago: «Mi plan».</summary>
    public const string PlanPath = "/app/plan";

    private static readonly CultureInfo Mexico = CultureInfo.GetCultureInfo("es-MX");

    /// <param name="lastProDay">Último día con Pro (en México).</param>
    /// <param name="extendedExistingPro">Ya tenía Pro pagado vigente: el pago extiende su plan.</param>
    public static string SubscriptionPaidBody(
        string? businessName, SubscriptionPackage package, decimal amount, DateOnly lastProDay, bool extendedExistingPro,
        int? founderNumber, string siteBaseUrl)
    {
        var site = siteBaseUrl.Trim().TrimEnd('/');
        var greeting = string.IsNullOrWhiteSpace(businessName) ? "¡Hola!" : $"¡Hola, {businessName.Trim()}!";
        var until = lastProDay.ToString("d 'de' MMMM 'de' yyyy", Mexico);
        var paragraphs = new List<string>
        {
            greeting,
            $"Recibimos tu pago de {amount.ToString("$#,0.00", Mexico)} por Pro ({package.Label().ToLower(Mexico)}).",
            $"Tienes Pro hasta el {until}.",
        };
        if (extendedExistingPro)
        {
            paragraphs.Add("Ya tenías Pro pagado: este pago extiende tu plan, no se pierde ningún día.");
        }

        if (founderNumber is { } number)
        {
            paragraphs.Add($"Eres el fundador número {number.ToString(Mexico)}: conservas el precio de fundador de por vida.");
        }

        return EmailLayout.Render(new EmailContent(
            SubscriptionPaidSubject,
            Preheader: $"Tu plan Pro quedó activo hasta el {until}.",
            Title: "¡Gracias por tu pago!",
            Paragraphs: paragraphs,
            ActionText: "Ver mi plan",
            ActionUrl: site + PlanPath,
            Footer: "Recibiste este correo porque pagaste Pro en Local133. El precio incluye IVA."),
            site);
    }

    public const string BankAccountActiveSubject = "Tu cuenta ya está activa: ya puedes cobrar · Local133";

    /// <summary>Ruta del botón: la pantalla de cobro (sin sesión, el sitio manda al login y regresa).</summary>
    public const string ChargePath = "/app/cobrar";

    /// <param name="businessName">Nombre del negocio, si ya lo tiene.</param>
    /// <param name="maskedClabe">CLABE enmascarada (<c>Clabe.Mask</c>); nunca la completa.</param>
    /// <param name="siteBaseUrl">Base absoluta del sitio.</param>
    public static string BankAccountActiveBody(string? businessName, string maskedClabe, string siteBaseUrl)
    {
        var site = siteBaseUrl.Trim().TrimEnd('/');
        var greeting = string.IsNullOrWhiteSpace(businessName) ? "¡Hola!" : $"¡Hola, {businessName.Trim()}!";
        return EmailLayout.Render(new EmailContent(
            BankAccountActiveSubject,
            Preheader: "Tu CLABE quedó lista en CoDi. Entra y haz tu primer cobro.",
            Title: "¡Ya puedes cobrar!",
            Paragraphs:
            [
                greeting,
                $"Tu cuenta bancaria {maskedClabe} ya está activa en CoDi.",
                "Desde hoy puedes cobrar con QR o enviar el cobro al celular de tu cliente. El dinero llega directo a tu cuenta.",
            ],
            ActionText: "Empezar a cobrar",
            ActionUrl: site + ChargePath,
            Footer: "Recibiste este correo porque registraste tu negocio en Local133."),
            site);
    }
}
