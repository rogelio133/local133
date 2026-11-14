using Local133.Application.Payments;
using Local133.Application.Sales;
using Local133.Infrastructure.Persistence;
using Local133.Web.Hubs;
using Local133.Web.Identity;
using Local133.Web.Jobs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Respawn;

namespace Local133.IntegrationTests.Infrastructure;

/// <summary>
/// Sitio completo sobre la base <c>Local133_Tests</c> del SQL Server local (recreada con migraciones,
/// limpiada con Respawn entre pruebas). Cadena por defecto: SQLEXPRESS; se cambia con <c>LOCAL133_TEST_DB</c>.
/// </summary>
public class Local133WebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Ruta /api que solo existe en pruebas: lanza un challenge de cookie sin sesión.</summary>
    public const string ProtectedApiPath = "/api/__test/protected";

    /// <summary>Ruta fuera de /api que lanza el mismo challenge (debe redirigir al login).</summary>
    public const string ProtectedSitePath = "/__test/protected";

    public static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("LOCAL133_TEST_DB")
        ?? @"Server=localhost\SQLEXPRESS;Database=Local133_Tests;Trusted_Connection=True;TrustServerCertificate=True";

    private Respawner? _respawner;

    public InMemoryEmailSender Emails { get; } = new();

    /// <summary>Pey simulado: ninguna prueba del sitio llama a la API real.</summary>
    public FakePaymentGateway Gateway { get; } = new();

    /// <summary>Avisos de pago en tiempo real enviados (además de enviarse por SignalR).</summary>
    public RecordingSaleNotifier Notifier { get; } = new();

    /// <summary>Secreto Standard Webhooks de prueba (<c>Pey:WebhookSecret</c>).</summary>
    public const string WebhookSecret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw";

    /// <summary>Lo que devuelve el proveedor externo falso; cada prueba lo ajusta (se restablece en <see cref="ResetAsync"/>).</summary>
    public FakeExternalUser ExternalUser { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        // Sin Hangfire (se lee al registrar servicios, por eso UseSetting): las pruebas llaman a los jobs directo.
        builder.UseSetting(JobsSetup.EnabledKey, "false");
        // Sin watcher de Sass: cada factory levantaría su propio `sass --watch` sobre wwwroot/css.
        builder.UseSetting(Program.SassWatchKey, "false");
        // Los user-secrets de desarrollo también se cargan aquí: el admin solo se siembra si la prueba lo pide
        // (fuente agregada al final, gana sobre user-secrets).
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [AdminSeeder.EmailKey] = "",
            [AdminSeeder.PasswordKey] = "",
            ["Pey:WebhookSecret"] = WebhookSecret,
        }));

        builder.ConfigureTestServices(services =>
        {
            // Reemplaza la conexión (user-secrets podría apuntar a la base de desarrollo).
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlServer(ConnectionString));

            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);

            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway>(Gateway);

            // Notificador real de SignalR envuelto: las pruebas ven a quién se avisó.
            services.RemoveAll<ISaleNotifier>();
            services.AddSingleton<SignalRSaleNotifier>();
            services.AddSingleton<ISaleNotifier>(sp =>
            {
                Notifier.Inner = sp.GetRequiredService<SignalRSaleNotifier>();
                return Notifier;
            });

            services.AddTransient<IStartupFilter, ChallengeEndpointsStartupFilter>();

            // Proveedor externo falso: las pruebas no dependen de Google/Facebook ni de sus secretos.
            services.AddSingleton(ExternalUser);
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, FakeExternalAuthHandler>(FakeExternalAuthHandler.SchemeName, "Proveedor", _ => { });
        });
    }

    public async Task InitializeAsync()
    {
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }

        _respawner = await Respawner.CreateAsync(ConnectionString, new RespawnerOptions
        {
            DbAdapter = DbAdapter.SqlServer,
            // Datos sembrados por migraciones: no se borran.
            TablesToIgnore = ["__EFMigrationsHistory", "AspNetRoles", "SystemSettings"],
        });
    }

    public async Task ResetAsync()
    {
        Emails.Clear();
        Gateway.Reset();
        Notifier.Reset();
        var defaults = new FakeExternalUser();
        ExternalUser.ProviderKey = defaults.ProviderKey;
        ExternalUser.Email = defaults.Email;
        if (_respawner is not null)
        {
            await _respawner.ResetAsync(ConnectionString);
        }
    }

    Task IAsyncLifetime.DisposeAsync() => base.DisposeAsync().AsTask();

    /// <summary>Middleware al inicio del pipeline que lanza un challenge en las rutas de prueba.</summary>
    private sealed class ChallengeEndpointsStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (ctx, nextMiddleware) =>
            {
                if (ctx.Request.Path == ProtectedApiPath || ctx.Request.Path == ProtectedSitePath)
                {
                    await ctx.ChallengeAsync(IdentityConstants.ApplicationScheme);
                    return;
                }
                await nextMiddleware(ctx);
            });
            next(app);
        };
    }
}

[CollectionDefinition(Name)]
public class WebCollection : ICollectionFixture<Local133WebFactory>
{
    public const string Name = "web";
}
