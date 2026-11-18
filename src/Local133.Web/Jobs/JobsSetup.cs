using Hangfire;
using Hangfire.SqlServer;
using Local133.Infrastructure;
using Local133.Infrastructure.Jobs;
using Microsoft.Data.SqlClient;

namespace Local133.Web.Jobs;

/// <summary>
/// Jobs en segundo plano con Hangfire (almacenamiento en la misma base, esquema <c>HangFire</c>, creado por Hangfire).
/// Se activan con <c>Jobs:Enabled</c> (por defecto <c>true</c>; las pruebas lo apagan y llaman a los jobs directo).
/// </summary>
public static class JobsSetup
{
    public const string EnabledKey = "Jobs:Enabled";

    /// <summary>Los jobs recurrentes se programan en hora del centro de México.</summary>
    public static readonly TimeZoneInfo MexicoCity = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");

    public static bool IsEnabled(IConfiguration configuration) => configuration.GetValue(EnabledKey, defaultValue: true);

    public static IServiceCollection AddAppJobs(this IServiceCollection services, IConfiguration configuration)
    {
        if (!IsEnabled(configuration))
        {
            return services;
        }

        var connectionString = configuration.GetConnectionString(DependencyInjection.ConnectionStringName);
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            // Los jobs son recurrentes: si uno falla, la siguiente corrida lo vuelve a intentar.
            .UseFilter(new AutomaticRetryAttribute { Attempts = 0 })
            .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
            {
                SqlClientFactory = SqlClientFactory.Instance,
                CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                QueuePollInterval = TimeSpan.Zero,
                UseRecommendedIsolationLevel = true,
                DisableGlobalLocks = true,
            }));
        services.AddHangfireServer(options => options.WorkerCount = 2);
        return services;
    }

    /// <summary>Programa (o actualiza) los jobs recurrentes.</summary>
    public static void ScheduleAppJobs(this WebApplication app)
    {
        if (!IsEnabled(app.Configuration))
        {
            return;
        }

        var jobs = app.Services.GetRequiredService<IRecurringJobManager>();
        var options = new RecurringJobOptions { TimeZone = MexicoCity };
        jobs.AddOrUpdate<BankAccountPollingJob>(BankAccountPollingJob.RecurringJobId, j => j.RunAsync(CancellationToken.None), BankAccountPollingJob.Cron, options);
        jobs.AddOrUpdate<SaleExpirationJob>(SaleExpirationJob.RecurringJobId, j => j.RunAsync(CancellationToken.None), SaleExpirationJob.Cron, options);
        jobs.AddOrUpdate<PaymentReconciliationJob>(PaymentReconciliationJob.RecurringJobId, j => j.RunAsync(CancellationToken.None), PaymentReconciliationJob.Cron, options);
    }
}
