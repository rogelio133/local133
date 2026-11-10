using Microsoft.AspNetCore.Identity;

namespace Local133.Infrastructure.Identity;

/// <summary>
/// Usuario de Identity. Guarda cuándo y qué versión de los documentos legales aceptó al registrarse
/// (<c>Local133.Domain.Legal.LegalDocuments</c>). El vendedor ligado es <c>Merchant.UserId</c>.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public DateTimeOffset? TermsAcceptedAt { get; set; }

    public string? TermsVersion { get; set; }

    public DateTimeOffset? PrivacyNoticeAcceptedAt { get; set; }

    public string? PrivacyNoticeVersion { get; set; }
}
