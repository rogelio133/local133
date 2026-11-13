namespace Local133.Infrastructure.Pey;

/// <summary>Configuración de la sección <c>Pey</c>. La API key va en user-secrets / variables de entorno.</summary>
public sealed class PeyOptions
{
    public const string SectionName = "Pey";

    public string BaseUrl { get; set; } = "https://api.pey.mx";

    /// <summary>API key de la plataforma (<c>Pey:ApiKey</c>). Sin ella el sitio arranca, pero cualquier llamada falla.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Secreto de firma del webhook (<c>Pey:WebhookSecret</c>, <c>whsec_…</c>, en portal.mipey.mx). Sin él el sitio arranca,
    /// pero <c>POST /api/webhooks/pey</c> responde 500 (Pey reintenta).
    /// </summary>
    public string? WebhookSecret { get; set; }

    /// <summary>Tiempo máximo de cada intento.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Tiempo máximo de la operación completa, con reintentos.</summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(90);

    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Espera base entre reintentos (crece exponencialmente, con jitter; respeta <c>Retry-After</c>).</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
}
