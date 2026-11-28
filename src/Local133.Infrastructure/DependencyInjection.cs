using Local133.Application.BankAccounts;
using Local133.Application.Merchants;
using Local133.Application.Notifications;
using Local133.Application.Payments;
using Local133.Application.Products;
using Local133.Application.Sales;
using Local133.Application.Storage;
using Local133.Application.Admin;
using Local133.Application.Subscriptions;
using Local133.Application.Tenancy;
using Local133.Application.Webhooks;
using Local133.Infrastructure.BankAccounts;
using Local133.Infrastructure.Email;
using Local133.Infrastructure.Jobs;
using Local133.Infrastructure.Merchants;
using Local133.Infrastructure.Notifications;
using Local133.Infrastructure.Payments;
using Local133.Infrastructure.Persistence;
using Local133.Infrastructure.Pey;
using Local133.Infrastructure.Products;
using Local133.Infrastructure.Sales;
using Local133.Infrastructure.Storage;
using Local133.Infrastructure.Admin;
using Local133.Infrastructure.Subscriptions;
using Local133.Infrastructure.Webhooks;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Local133.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Default";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Falta la cadena de conexión 'ConnectionStrings:{ConnectionStringName}'. Configúrala con " +
                $"dotnet user-secrets set \"ConnectionStrings:{ConnectionStringName}\" \"<cadena>\" --project src/Local133.Web");
        }

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
                sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

        // Correo: Resend si existe Resend:ApiKey (user-secrets / Key Vault); si no, se guarda en disco.
        services.AddEmail(configuration);
        // Base de las ligas de los correos que mandan webhooks y jobs (sin petición): App:PublicBaseUrl.
        services.Configure<SiteUrlOptions>(configuration.GetSection(SiteUrlOptions.SectionName));

        // Archivos (fotos de productos): disco local (Storage:LocalDirectory); en producción se cambia por blob.
        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));
        services.AddSingleton<IFileStorage>(sp => new LocalDiskFileStorage(
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<FileStorageOptions>>(),
            sp.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>()));
        services.AddSingleton<IProductPhotoProcessor, SkiaProductPhotoProcessor>();
        // «Mi información» (4.1): logo en PNG con transparencia y foto del local en JPEG.
        services.AddSingleton<IMerchantImageProcessor, SkiaMerchantImageProcessor>();

        // Pey (CoDi): una API key para todos los vendedores; la key va en user-secrets (Pey:ApiKey).
        services.AddPeyGateway(configuration);

        // Sin alcance por defecto (jobs, webhook, dotnet ef); el sitio lo reemplaza por el vendedor de la sesión.
        services.TryAddScoped<ITenantContext, SystemTenantContext>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IBankAccountRegistration, BankAccountRegistration>();
        // Botón "Verificar estatus", job de sondeo y webhook bank.account.updated comparten este caso de uso.
        services.AddScoped<IBankAccountStatusSync, BankAccountStatusSync>();
        // Correo «Tu cuenta ya está activa» (alta, webhook, sondeo o botón): uno por vendedor.
        services.AddScoped<IBankAccountActivationNotifier, BankAccountActivationEmailNotifier>();
        services.AddScoped<ISaleCharging, SaleCharging>();
        services.AddScoped<ISaleExpiration, SaleExpiration>();
        services.AddScoped<IPaymentRecorder, PaymentRecorder>();
        // Respaldo del webhook: el job de conciliación pregunta a Pey por las ventas sin pagar.
        services.AddScoped<IPaymentReconciliation, PaymentReconciliation>();
        services.AddScoped<IPeyWebhookProcessor, PeyWebhookProcessor>();
        services.AddScoped<IProductCatalog, ProductCatalog>();
        services.AddScoped<IProductCategories, ProductCategoriesService>();
        services.AddScoped<IMerchantProfile, MerchantProfileService>();
        services.AddScoped<IPublicCatalogSettings, PublicCatalogSettings>();
        // Estado del plan (suscripción) del vendedor, con caché por petición.
        services.AddScoped<IPlanAccess, PlanAccess>();
        // Prueba de Pro al primer cobro pagado (5.13): la llaman PaymentRecorder y el respaldo de suscripciones-diario.
        services.AddScoped<ITrialStarter, TrialStarter>();
        // Uso justo (5.14): tras cada pago se revisa si el vendedor cruzó los cobros concretados del mes.
        services.AddScoped<IChargeLimitAlerts, ChargeLimitAlerts>();
        // Por ahora, solo el log (el tablero del admin lo calcula con su propia consulta).
        services.TryAddSingleton<IChargeLimitNotifier, LoggingChargeLimitNotifier>();
        // Pago de la suscripción con link de Pey (5.6): checkout, abono (webhook y conciliación) y correo.
        services.AddScoped<ISubscriptionCheckout, SubscriptionCheckout>();
        services.AddScoped<SubscriptionCrediting>();
        services.AddScoped<ISubscriptionPaymentRecorder, SubscriptionPaymentRecorder>();
        services.AddScoped<ReferralRewardNotifier>();
        services.AddScoped<ISubscriptionPaymentReconciliation, SubscriptionPaymentReconciliation>();
        services.AddScoped<ISubscriptionPaymentNotifier, SubscriptionPaymentEmailNotifier>();
        // Avisos en el sitio con correo opcional, idempotentes por DedupKey (base de la 5.4, adelantada en la 5.7).
        services.AddScoped<INotificationService, NotificationService>();
        // Resumen mensual del día 1 (5.7 lo llama; 5.10: aviso y correo). La clase concreta también se registra para que
        // las pruebas la usen aunque cambien la interfaz por un registrador.
        services.AddScoped<MonthlySummaryNotifier>();
        services.AddScoped<IMonthlySummaryNotifier>(sp => sp.GetRequiredService<MonthlySummaryNotifier>());
        // Admin (6.2): acciones sobre el plan de un vendedor y parámetros globales, con auditoría.
        services.AddScoped<IAdminPlanActions, AdminPlanActions>();
        services.AddScoped<IAdminWebhookActions, AdminWebhookActions>();
        services.AddScoped<ISystemSettingsEditor, SystemSettingsEditor>();
        // Tiempo real: el sitio registra el de SignalR antes; aquí solo el vacío para jobs y dotnet ef.
        services.TryAddSingleton<ISaleNotifier, NullSaleNotifier>();

        // Jobs (Hangfire los activa desde DI; el sitio los programa si Jobs:Enabled).
        services.AddScoped<BankAccountPollingJob>();
        services.AddScoped<SaleExpirationJob>();
        services.AddScoped<PaymentReconciliationJob>();
        services.AddScoped<PaymentLinkReconciliationJob>();
        services.AddScoped<SubscriptionDailyJob>();

        return services;
    }
}
