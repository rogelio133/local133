using Local133.Infrastructure.Email;
using Local133.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        // Correo: en desarrollo se guarda en disco; el SMTP real llega con Notificaciones.
        services.Configure<EmailPickupOptions>(configuration.GetSection(EmailPickupOptions.SectionName));
        services.AddTransient<IEmailSender, FileEmailSender>();

        return services;
    }
}
