namespace Local133.Infrastructure.Email;

/// <summary>Correos de avisos al vendedor que salen de webhooks y jobs (sin petición HTTP).</summary>
public static class MerchantEmails
{
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
