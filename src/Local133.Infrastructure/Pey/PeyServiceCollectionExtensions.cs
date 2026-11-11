using System.Net;
using System.Net.Http.Headers;

using Local133.Application.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace Local133.Infrastructure.Pey;

public static class PeyServiceCollectionExtensions
{
    /// <summary>
    /// Registra <see cref="IPaymentGateway"/> (Pey) como <c>HttpClient</c> tipado con el pipeline estándar de
    /// resiliencia: timeout por intento y total, circuit breaker y reintentos con backoff exponencial + jitter.
    /// </summary>
    /// <remarks>
    /// Reintentos por método: los GET en cualquier falla transitoria (5xx, 408, 429, red, timeout); los POST
    /// solo en 503/429, porque Pey no tiene llave de idempotencia y repetir un alta tras un 500 o un timeout
    /// podría duplicar un QR o mandar dos notificaciones push.
    /// </remarks>
    public static IServiceCollection AddPeyGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PeyOptions>(configuration.GetSection(PeyOptions.SectionName));

        services.AddHttpClient<IPaymentGateway, PeyClient>((sp, client) =>
            {
                var pey = sp.GetRequiredService<IOptions<PeyOptions>>().Value;
                client.BaseAddress = new Uri(pey.BaseUrl.TrimEnd('/') + "/");
                client.Timeout = Timeout.InfiniteTimeSpan; // los tiempos los controla el pipeline
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Local133", "0.1"));
                if (!string.IsNullOrWhiteSpace(pey.ApiKey))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pey.ApiKey);
                }
            })
            .AddStandardResilienceHandler()
            .Configure((resilience, sp) =>
            {
                var pey = sp.GetRequiredService<IOptions<PeyOptions>>().Value;
                resilience.AttemptTimeout.Timeout = pey.AttemptTimeout;
                resilience.TotalRequestTimeout.Timeout = pey.TotalTimeout;
                // La validación exige un muestreo de al menos el doble del timeout por intento.
                var sampling = pey.AttemptTimeout * 2;
                resilience.CircuitBreaker.SamplingDuration = sampling > TimeSpan.FromSeconds(30) ? sampling : TimeSpan.FromSeconds(30);
                resilience.Retry.MaxRetryAttempts = pey.MaxRetryAttempts;
                resilience.Retry.Delay = pey.RetryDelay;
                resilience.Retry.BackoffType = DelayBackoffType.Exponential;
                resilience.Retry.UseJitter = true;
                resilience.Retry.ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Context, args.Outcome));
            });

        return services;
    }

    private static bool ShouldRetry(ResilienceContext context, Outcome<HttpResponseMessage> outcome)
    {
        if (context.GetRequestMessage()?.Method == HttpMethod.Get)
        {
            return HttpClientResiliencePredicates.IsTransient(outcome);
        }

        // POST: Pey no procesó la petición (mantenimiento o límite de peticiones).
        return outcome.Result?.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests;
    }
}
