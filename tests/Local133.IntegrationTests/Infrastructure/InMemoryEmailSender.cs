using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace Local133.IntegrationTests.Infrastructure;

public sealed record SentEmail(string To, string Subject, string Html)
{
    /// <summary>Primera liga del cuerpo, decodificada.</summary>
    public string Link =>
        Regex.Match(Html, "href=\"([^\"]+)\"") is { Success: true } m
            ? WebUtility.HtmlDecode(m.Groups[1].Value)
            : throw new InvalidOperationException($"El correo '{Subject}' no trae liga.");
}

/// <summary>Captura los correos en memoria para leer las ligas de confirmación y recuperación.</summary>
public sealed class InMemoryEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<SentEmail> _sent = new();

    public IReadOnlyList<SentEmail> Sent => _sent.ToList();

    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        _sent.Enqueue(new SentEmail(email, subject, htmlMessage));
        return Task.CompletedTask;
    }

    public SentEmail Last(string to) =>
        Sent.LastOrDefault(e => string.Equals(e.To, to, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"No se envió correo a {to}.");

    public void Clear() => _sent.Clear();
}
