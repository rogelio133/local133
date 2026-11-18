using System.Net;
using System.Net.Http;

using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Timeout;
using Resend;

namespace Local133.Infrastructure.Email;

public static class EmailServiceCollectionExtensions
{
    /// <summary>
    /// Registra <see cref="IEmailSender"/>: Resend si existe <c>Resend:ApiKey</c>; si no, <see cref="FileEmailSender"/>
    /// (guarda cada correo como .html en <c>EmailPickup:Directory</c>).
    /// </summary>
    /// <remarks>
    /// Reintentos (Resend tiene llave de idempotencia, así que repetir un POST no duplica el correo): red, timeout,
    /// 5xx y 429, respetando <c>Retry-After</c>. Los demás 4xx no se reintentan.
    /// </remarks>
    public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EmailPickupOptions>(configuration.GetSection(EmailPickupOptions.SectionName));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));

        var resendSection = configuration.GetSection(ResendEmailOptions.SectionName);
        if (string.IsNullOrWhiteSpace(resendSection[nameof(ResendEmailOptions.ApiKey)]))
        {
            services.AddTransient<IEmailSender, FileEmailSender>();
            return services;
        }

        services.Configure<ResendEmailOptions>(resendSection);
        // Ojo: con ApiToken vacío el SDK usaría la variable de entorno RESEND_API_KEY; por eso solo se registra con llave.
        services.AddOptions<ResendClientOptions>()
            .Configure<IOptions<ResendEmailOptions>>((client, resend) =>
            {
                client.ApiToken = resend.Value.ApiKey!;
                client.ApiUrl = resend.Value.ApiUrl;
                client.ThrowExceptions = true;
            });

        services.AddHttpClient<IResend, ResendClient>(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .AddStandardResilienceHandler()
            .Configure((resilience, sp) =>
            {
                var resend = sp.GetRequiredService<IOptions<ResendEmailOptions>>().Value;
                resilience.AttemptTimeout.Timeout = resend.AttemptTimeout;
                resilience.TotalRequestTimeout.Timeout = resend.TotalTimeout;
                // La validación exige un muestreo de al menos el doble del timeout por intento.
                var sampling = resend.AttemptTimeout * 2;
                resilience.CircuitBreaker.SamplingDuration = sampling > TimeSpan.FromSeconds(30) ? sampling : TimeSpan.FromSeconds(30);
                resilience.Retry.MaxRetryAttempts = resend.MaxRetryAttempts;
                resilience.Retry.Delay = resend.RetryDelay;
                resilience.Retry.BackoffType = DelayBackoffType.Exponential;
                resilience.Retry.UseJitter = true;
                resilience.Retry.ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Outcome));
            });

        services.AddTransient<IEmailSender, ResendEmailSender>();
        return services;
    }

    private static bool ShouldRetry(Outcome<HttpResponseMessage> outcome) =>
        outcome.Exception is HttpRequestException or TimeoutRejectedException
        || outcome.Result?.StatusCode is HttpStatusCode.TooManyRequests
        || (int?)outcome.Result?.StatusCode >= 500;
}
