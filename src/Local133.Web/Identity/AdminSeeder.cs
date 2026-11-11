using Local133.Application.Auth;
using Local133.Infrastructure.Identity;
using Local133.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace Local133.Web.Identity;

public enum AdminSeedResult
{
    /// <summary>Se creó el usuario (correo confirmado) con rol Admin.</summary>
    Created,

    /// <summary>Ya existía con rol Admin; no se cambió nada (ni la contraseña).</summary>
    AlreadyAdmin,

    /// <summary>Existía sin roles; se le agregó Admin.</summary>
    Promoted,

    /// <summary>El correo pertenece a un vendedor: no se mezclan roles.</summary>
    RejectedMerchant,

    /// <summary>Identity rechazó el alta (p. ej. contraseña fuera de la política); no se creó nada.</summary>
    Failed,
}

/// <summary>
/// Siembra del usuario administrador a partir de la configuración <c>Seed:Admin:Email</c> y
/// <c>Seed:Admin:Password</c> (user-secrets en desarrollo, variables de entorno en producción). Es idempotente:
/// si el usuario ya existe no se cambia su contraseña.
/// </summary>
public partial class AdminSeeder(UserManager<ApplicationUser> userManager, AppDbContext db, ILogger<AdminSeeder> logger)
{
    public const string EmailKey = "Seed:Admin:Email";
    public const string PasswordKey = "Seed:Admin:Password";

    public async Task<AdminSeedResult> SeedAsync(string email, string password)
    {
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            if (await userManager.IsInRoleAsync(existing, Roles.Admin))
            {
                LogAlreadyAdmin(email);
                return AdminSeedResult.AlreadyAdmin;
            }
            if (await userManager.IsInRoleAsync(existing, Roles.Merchant))
            {
                LogRejectedMerchant(email);
                return AdminSeedResult.RejectedMerchant;
            }

            var promoted = await userManager.AddToRoleAsync(existing, Roles.Admin);
            if (!promoted.Succeeded)
            {
                LogFailed(email, Describe(promoted));
                return AdminSeedResult.Failed;
            }
            LogPromoted(email);
            return AdminSeedResult.Promoted;
        }

        // UserManager guarda en cada llamada sobre el mismo DbContext (scoped): la transacción abarca usuario y rol.
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            result = await userManager.AddToRoleAsync(user, Roles.Admin);
        }
        if (!result.Succeeded)
        {
            await transaction.RollbackAsync();
            LogFailed(email, Describe(result));
            return AdminSeedResult.Failed;
        }

        await transaction.CommitAsync();
        LogCreated(email);
        return AdminSeedResult.Created;
    }

    private static string Describe(IdentityResult result) => string.Join(" ", result.Errors.Select(e => e.Description));

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin sembrado: se creó {Email}.")]
    private partial void LogCreated(string email);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Admin sembrado: {Email} ya es administrador.")]
    private partial void LogAlreadyAdmin(string email);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin sembrado: {Email} ya existía; se le agregó el rol Admin.")]
    private partial void LogPromoted(string email);

    [LoggerMessage(Level = LogLevel.Error, Message = "Admin sembrado: {Email} es un vendedor; no se le asigna el rol Admin. Usa otro correo en Seed:Admin:Email.")]
    private partial void LogRejectedMerchant(string email);

    [LoggerMessage(Level = LogLevel.Error, Message = "Admin sembrado: no se pudo crear {Email}: {Errors}")]
    private partial void LogFailed(string email, string errors);
}

public static class AdminSeederExtensions
{
    /// <summary>
    /// Si hay <c>Seed:Admin:Email</c> y <c>Seed:Admin:Password</c>, crea el admin al arrancar. Una falla se registra
    /// en el log y el sitio sigue arrancando.
    /// </summary>
    public static async Task SeedAdminAsync(this WebApplication app)
    {
        var email = app.Configuration[AdminSeeder.EmailKey];
        var password = app.Configuration[AdminSeeder.PasswordKey];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            app.Logger.LogDebug("Sin {EmailKey}/{PasswordKey}: no se siembra el admin.", AdminSeeder.EmailKey, AdminSeeder.PasswordKey);
            return;
        }

        try
        {
            await using var scope = app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AdminSeeder>().SeedAsync(email.Trim(), password);
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "No se pudo sembrar el admin.");
        }
    }
}
