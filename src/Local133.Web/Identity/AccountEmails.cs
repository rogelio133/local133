using System.Text.Encodings.Web;

namespace Local133.Web.Identity;

/// <summary>Asuntos y cuerpos (HTML) de los correos de la cuenta.</summary>
public static class AccountEmails
{
    public const string ConfirmSubject = "Confirma tu correo · Local133";
    public const string ResetSubject = "Restablece tu contraseña · Local133";
    public const string ChangeEmailSubject = "Confirma tu nuevo correo · Local133";

    public static string ConfirmBody(string callbackUrl) => Body(
        "¡Bienvenido a Local133!",
        "Para activar tu cuenta confirma tu correo:",
        "Confirmar mi correo",
        callbackUrl,
        "Si no creaste una cuenta, ignora este mensaje.");

    public static string ResetBody(string callbackUrl) => Body(
        "Restablecer contraseña",
        "Recibimos una solicitud para cambiar la contraseña de tu cuenta:",
        "Elegir nueva contraseña",
        callbackUrl,
        "Si no la pediste tú, ignora este mensaje; tu contraseña no cambia.");

    public static string ChangeEmailBody(string callbackUrl) => Body(
        "Confirma tu nuevo correo",
        "Para usar este correo en tu cuenta de Local133 confírmalo:",
        "Confirmar nuevo correo",
        callbackUrl,
        "Si no pediste el cambio, ignora este mensaje.");

    private static string Body(string title, string intro, string action, string url, string footer)
    {
        var href = HtmlEncoder.Default.Encode(url);
        return $"""
            <h2>{title}</h2>
            <p>{intro}</p>
            <p><a href="{href}">{action}</a></p>
            <p style="color:#6c757d;font-size:small">{footer}</p>
            """;
    }
}
