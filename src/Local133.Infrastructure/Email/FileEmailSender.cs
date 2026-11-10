using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Local133.Infrastructure.Email;

public class EmailPickupOptions
{
    public const string SectionName = "EmailPickup";

    /// <summary>Carpeta donde se guardan los correos (relativa a la raíz del contenido si no es absoluta).</summary>
    public string Directory { get; set; } = "App_Data/emails";
}

/// <summary>
/// <see cref="IEmailSender"/> de desarrollo: guarda cada correo como .html y escribe la primera liga en el log.
/// El envío real (SMTP) llega con el módulo de Notificaciones.
/// </summary>
public partial class FileEmailSender(
    IOptions<EmailPickupOptions> options,
    IWebHostEnvironment environment,
    ILogger<FileEmailSender> logger) : IEmailSender
{
    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        var directory = Path.IsPathRooted(options.Value.Directory)
            ? options.Value.Directory
            : Path.Combine(environment.ContentRootPath, options.Value.Directory);
        System.IO.Directory.CreateDirectory(directory);

        var fileName = $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}_{Sanitize(email)}_{Sanitize(subject)}.html";
        var path = Path.Combine(directory, fileName);
        var document = $"""
            <!DOCTYPE html>
            <html lang="es-MX"><head><meta charset="utf-8"><title>{System.Net.WebUtility.HtmlEncode(subject)}</title></head>
            <body><p><strong>Para:</strong> {System.Net.WebUtility.HtmlEncode(email)}<br><strong>Asunto:</strong> {System.Net.WebUtility.HtmlEncode(subject)}</p><hr>{htmlMessage}</body></html>
            """;
        await File.WriteAllTextAsync(path, document);

        var link = HrefRegex().Match(htmlMessage) is { Success: true } m ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value) : null;
        logger.LogInformation("Correo guardado en {Path} · Para {Email} · {Subject} · Liga: {Link}", path, email, subject, link);
    }

    private static string Sanitize(string value)
    {
        var cleaned = new string(value.Select(c => char.IsLetterOrDigit(c) || c is '@' or '.' or '-' ? c : '_').ToArray());
        return cleaned.Length > 40 ? cleaned[..40] : cleaned;
    }

    [GeneratedRegex("href=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase)]
    private static partial Regex HrefRegex();
}
