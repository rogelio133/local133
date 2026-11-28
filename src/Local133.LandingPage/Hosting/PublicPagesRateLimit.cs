using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Local133.LandingPage.Hosting;

/// <summary>
/// Límite de peticiones por IP del recibo y del catálogo públicos (sección <c>RateLimiting</c>, ajustes 2026-11-27).
/// Cada visita es una llamada a la API del portal, así que el límite va aquí: la API solo acepta la llave de la portada.
/// Ventana deslizante de <see cref="WindowSeconds"/> en 6 segmentos, sin cola: al pasarse, 429 con <c>Retry-After</c>.
/// </summary>
public sealed class PublicPagesRateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Recibos por IP en la ventana. Un cliente abre uno o dos; más es un script probando tokens.</summary>
    public int ReceiptPermitLimit { get; set; } = 30;

    /// <summary>Páginas del catálogo por IP en la ventana (búsquedas y páginas incluidas).</summary>
    public int StorefrontPermitLimit { get; set; } = 60;

    public int WindowSeconds { get; set; } = 60;
}

public static class PublicPagesRateLimit
{
    /// <summary>Política de <c>/recibo/{token}</c>.</summary>
    public const string ReceiptPolicy = "recibo";

    /// <summary>Política de <c>/tienda/{slug}</c>.</summary>
    public const string StorefrontPolicy = "tienda";

    private const int SegmentsPerWindow = 6;

    /// <summary>
    /// Registra las dos políticas. La IP es <c>RemoteIpAddress</c>, que <c>UseForwardedHeaders</c> (Program.cs) cambia por
    /// la del cliente que reporta el front-end de App Service; sin eso, todos los visitantes compartirían un límite.
    /// </summary>
    public static IServiceCollection AddPublicPagesRateLimit(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PublicPagesRateLimitOptions>(configuration.GetSection(PublicPagesRateLimitOptions.SectionName));
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(ReceiptPolicy, context => PartitionByIp(context, ReceiptPolicy, o => o.ReceiptPermitLimit));
            limiter.AddPolicy(StorefrontPolicy, context => PartitionByIp(context, StorefrontPolicy, o => o.StorefrontPermitLimit));
            limiter.OnRejected = async (rejected, cancellationToken) =>
            {
                var response = rejected.HttpContext.Response;
                // La ventana deslizante no informa RetryAfter: un segmento (WindowSeconds / 6) es lo que tarda en
                // liberarse el permiso más viejo.
                if (!rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    var options = rejected.HttpContext.RequestServices.GetRequiredService<IOptions<PublicPagesRateLimitOptions>>().Value;
                    retryAfter = TimeSpan.FromSeconds(Math.Max(1, options.WindowSeconds) / (double)SegmentsPerWindow);
                }

                response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

                response.Headers.CacheControl = "no-store";
                response.ContentType = "text/plain; charset=utf-8";
                await response.WriteAsync("Demasiadas solicitudes. Espera un momento y vuelve a intentar.", cancellationToken);
            };
        });
        return services;
    }

    private static RateLimitPartition<string> PartitionByIp(
        HttpContext context, string policy, Func<PublicPagesRateLimitOptions, int> permitLimit)
    {
        var options = context.RequestServices.GetRequiredService<IOptions<PublicPagesRateLimitOptions>>().Value;
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "desconocida";
        return RateLimitPartition.GetSlidingWindowLimiter($"{policy}:{ip}", _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, permitLimit(options)),
            Window = TimeSpan.FromSeconds(Math.Max(1, options.WindowSeconds)),
            SegmentsPerWindow = SegmentsPerWindow,
            QueueLimit = 0,
        });
    }
}
