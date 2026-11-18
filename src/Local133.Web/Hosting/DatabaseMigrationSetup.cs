using Local133.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Hosting;

/// <summary>
/// Aplica al arrancar las migraciones pendientes de EF Core (crea la base si no existe). Se activa con
/// <c>Database:MigrateOnStartup</c> (por defecto <c>true</c>; las pruebas lo apagan y migran desde su fábrica).
/// </summary>
/// <remarks>
/// Va antes de sembrar el admin (necesita los roles de la migración <c>Identity</c>) y de programar los jobs
/// (Hangfire crea su esquema, pero no la base). Si una migración falla, la excepción sube y el sitio no arranca:
/// mejor eso que atender con un esquema viejo. Varias instancias a la vez no chocan: EF Core toma un candado
/// de migración en SQL Server.
/// </remarks>
public static class DatabaseMigrationSetup
{
    public const string MigrateOnStartupKey = "Database:MigrateOnStartup";

    /// <summary>Tiempo por comando: algunas migraciones rellenan columnas con un <c>UPDATE</c> sobre tablas completas.</summary>
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(5);

    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        if (!app.Configuration.GetValue(MigrateOnStartupKey, defaultValue: true))
        {
            app.Logger.LogDebug("{Key}=false: no se aplican migraciones al arrancar.", MigrateOnStartupKey);
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.SetCommandTimeout(CommandTimeout);

        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count == 0)
        {
            app.Logger.LogInformation("Base de datos al día: sin migraciones pendientes.");
            return;
        }

        app.Logger.LogInformation("Aplicando {Count} migraciones pendientes: {Migrations}", pending.Count, string.Join(", ", pending));
        await db.Database.MigrateAsync();
        app.Logger.LogInformation("Migraciones aplicadas.");
    }
}
