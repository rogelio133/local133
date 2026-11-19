using Local133.Application.BankAccounts;
using Local133.Application.Payments;
using Local133.Application.Products;
using Local133.Application.Sales;
using Local133.Application.Storage;
using Local133.Application.Tenancy;
using Local133.Application.Webhooks;
using Local133.Infrastructure.BankAccounts;
using Local133.Infrastructure.Email;
using Local133.Infrastructure.Jobs;
using Local133.Infrastructure.Payments;
using Local133.Infrastructure.Persistence;
using Local133.Infrastructure.Pey;
using Local133.Infrastructure.Products;
using Local133.Infrastructure.Sales;
using Local133.Infrastructure.Storage;
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
        // Tiempo real: el sitio registra el de SignalR antes; aquí solo el vacío para jobs y dotnet ef.
        services.TryAddSingleton<ISaleNotifier, NullSaleNotifier>();

        // Jobs (Hangfire los activa desde DI; el sitio los programa si Jobs:Enabled).
        services.AddScoped<BankAccountPollingJob>();
        services.AddScoped<SaleExpirationJob>();
        services.AddScoped<PaymentReconciliationJob>();

        return services;
    }
}
