namespace Local133.Domain.Legal;

/// <summary>
/// Versión vigente de los documentos legales. Al cambiar el texto de una página se sube su versión; cada
/// usuario guarda la versión que aceptó al registrarse.
/// </summary>
public static class LegalDocuments
{
    public const int VersionMaxLength = 20;

    public const string CurrentTermsVersion = "2026-11-10";
    public const string CurrentPrivacyNoticeVersion = "2026-11-10";
}
