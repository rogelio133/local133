namespace Local133.Infrastructure.Email;

/// <summary>Remitente de los correos (sección <c>Email</c>).</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// Nombre y dirección que ve quien recibe; debe ser de un dominio verificado en Resend
    /// (<c>notificaciones.local133.com.mx</c>, en <c>appsettings.json</c>).
    /// </summary>
    public string From { get; set; } = "Local133 <hola@notificaciones.local133.com.mx>";
}

/// <summary>Configuración de la sección <c>Resend</c>. La API key va en user-secrets / Key Vault.</summary>
public sealed class ResendEmailOptions
{
    public const string SectionName = "Resend";

    /// <summary>
    /// API key de solo envío (<c>Resend:ApiKey</c>). Sin ella los correos se guardan en disco (<see cref="FileEmailSender"/>).
    /// </summary>
    public string? ApiKey { get; set; }

    public string ApiUrl { get; set; } = "https://api.resend.com";

    /// <summary>Tiempo máximo de cada intento.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Tiempo máximo del envío completo, con reintentos (la página de Identity espera el envío).</summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Espera base entre reintentos (crece exponencialmente, con jitter; respeta <c>Retry-After</c>).</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
}
