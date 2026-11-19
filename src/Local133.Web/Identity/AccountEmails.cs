using Local133.Infrastructure.Email;

namespace Local133.Web.Identity;

/// <summary>Asuntos y cuerpos (HTML, con el layout de la marca <see cref="EmailLayout"/>) de los correos de la cuenta.</summary>
public static class AccountEmails
{
    public const string ConfirmSubject = "Confirma tu correo · Local133";
    public const string ResetSubject = "Restablece tu contraseña · Local133";
    public const string ChangeEmailSubject = "Confirma tu nuevo correo · Local133";

    public static string ConfirmBody(string callbackUrl) => Body(
        ConfirmSubject,
        "Confirma tu correo para activar tu cuenta de Local133.",
        "¡Bienvenido a Local133!",
        "Para activar tu cuenta confirma tu correo:",
        "Confirmar mi correo",
        callbackUrl,
        "Si no creaste una cuenta, ignora este mensaje.");

    public static string ResetBody(string callbackUrl) => Body(
        ResetSubject,
        "Elige una contraseña nueva para tu cuenta de Local133.",
        "Restablecer contraseña",
        "Recibimos una solicitud para cambiar la contraseña de tu cuenta:",
        "Elegir nueva contraseña",
        callbackUrl,
        "Si no la pediste tú, ignora este mensaje; tu contraseña no cambia.");

    public static string ChangeEmailBody(string callbackUrl) => Body(
        ChangeEmailSubject,
        "Confirma el nuevo correo de tu cuenta de Local133.",
        "Confirma tu nuevo correo",
        "Para usar este correo en tu cuenta de Local133 confírmalo:",
        "Confirmar nuevo correo",
        callbackUrl,
        "Si no pediste el cambio, ignora este mensaje.");

    private static string Body(
        string subject, string preheader, string title, string intro, string action, string url, string footer) =>
        EmailLayout.Render(
            new EmailContent(subject, preheader, title, [intro], action, url, footer),
            SiteBaseOf(url));

    /// <summary>
    /// La liga ya es absoluta (la arma la página con el host de la petición o <c>App:PublicBaseUrl</c>): de ahí sale la
    /// base del logo, sin que las páginas de Identity tengan que pasarla.
    /// </summary>
    private static string SiteBaseOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : SiteUrlOptions.DevelopmentFallbackBaseUrl;
}
