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
        await File.WriteAllTextAsync(path, Document(email, subject, htmlMessage));

        var link = HrefRegex().Match(htmlMessage) is { Success: true } m ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value) : null;
        logger.LogInformation("Correo guardado en {Path} · Para {Email} · {Subject} · Liga: {Link}", path, email, subject, link);
    }

    /// <summary>
    /// Documento guardado: el correo con una franja «Para / Asunto» arriba. Si el correo ya es un documento completo
    /// (<see cref="EmailLayout"/>), la franja va justo después de su <c>&lt;body&gt;</c>; si es un fragmento, se envuelve.
    /// </summary>
    public static string Document(string email, string subject, string htmlMessage)
    {
        var header = $"""<p style="margin:0;padding:8px 12px;font:14px/1.4 Arial,sans-serif;background:#FFFFFF;border-bottom:1px solid #DDDDDD;"><strong>Para:</strong> {System.Net.WebUtility.HtmlEncode(email)}<br><strong>Asunto:</strong> {System.Net.WebUtility.HtmlEncode(subject)}</p>""";
        if (BodyTagRegex().Match(htmlMessage) is { Success: true } body)
        {
            var at = body.Index + body.Length;
            return string.Concat(htmlMessage.AsSpan(0, at), header, htmlMessage.AsSpan(at));
        }

        return $"""
            <!DOCTYPE html>
            <html lang="es-MX"><head><meta charset="utf-8"><title>{System.Net.WebUtility.HtmlEncode(subject)}</title></head>
            <body>{header}<hr>{htmlMessage}</body></html>
            """;
    }

    private static string Sanitize(string value)
    {
        var cleaned = new string(value.Select(c => char.IsLetterOrDigit(c) || c is '@' or '.' or '-' ? c : '_').ToArray());
        return cleaned.Length > 40 ? cleaned[..40] : cleaned;
    }

    [GeneratedRegex("href=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase)]
    private static partial Regex HrefRegex();

    [GeneratedRegex("<body[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BodyTagRegex();
}
