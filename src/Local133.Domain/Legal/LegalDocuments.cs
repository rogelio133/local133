namespace Local133.Domain.Legal;

/// <summary>
/// Versión vigente de los documentos legales. Al cambiar el texto de una página se sube su versión; cada
/// usuario guarda la versión que aceptó al registrarse, y cada pedido de la suscripción la que aceptó al pagar (5.15).
/// </summary>
/// <remarks>
/// <c>2026-11-27</c> (5.18): planes Básico y Pro sin gratuito, prueba de 15 días al primer cobro, vencimiento sin cobros,
/// cambio de plan y aceptación en cada pedido. <c>2026-11-24</c>: la versión de la 5.11, con el plan gratuito.
/// </remarks>
public static class LegalDocuments
{
    public const int VersionMaxLength = 20;

    public const string CurrentTermsVersion = "2026-11-27";
    public const string CurrentPrivacyNoticeVersion = "2026-11-27";
}
