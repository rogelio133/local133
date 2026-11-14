using Local133.Application.Tenancy;
using Local133.Infrastructure.Identity;
using Local133.Infrastructure.Persistence;
using Local133.Web.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Local133.Web.Identity;

public static class IdentitySetup
{
    /// <summary>
    /// Identity con cookie y roles. Se usa <c>AddIdentity</c> (no <c>AddDefaultIdentity</c>/<c>AddDefaultUI</c>):
    /// solo existen las páginas scaffoldeadas en Areas/Identity; las demás (2FA, datos personales) dan 404.
    /// </summary>
    public static IServiceCollection AddAppIdentity(
        this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        services
            .AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.SignIn.RequireConfirmedAccount = true;
                options.SignIn.RequireConfirmedEmail = true;
                options.User.RequireUniqueEmail = true;

                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders()
            .AddErrorDescriber<SpanishIdentityErrorDescriber>()
            .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>();

        // Tenant por petición: el vendedor de la sesión (claim local133:merchant_id) filtra sus entidades en EF Core.
        services.AddHttpContextAccessor();
        services.Replace(ServiceDescriptor.Scoped<ITenantContext, HttpTenantContext>());

        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Identity/Account/Login";
            options.LogoutPath = "/Identity/Account/Logout";
            options.AccessDeniedPath = "/Identity/Account/AccessDenied";
            options.Cookie.Name = "local133.auth";
            options.Cookie.HttpOnly = true;
            // El perfil local es http; fuera de Development la cookie solo viaja por HTTPS.
            options.Cookie.SecurePolicy = environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;

            // /api no redirige a la página de login: responde 401/403.
            options.Events.OnRedirectToLogin = ctx => RedirectOrStatus(ctx, StatusCodes.Status401Unauthorized);
            options.Events.OnRedirectToAccessDenied = ctx => RedirectOrStatus(ctx, StatusCodes.Status403Forbidden);
        });

        services.AddExternalLogins(configuration);

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<AccountRegistration>();
        services.AddScoped<AdminSeeder>();

        return services;
    }

    /// <summary>
    /// Google y Facebook solo se registran si sus credenciales están en la configuración (user-secrets o
    /// variables de entorno). Sin ellas no aparecen los botones y el sitio funciona igual (CI, pruebas).
    /// Callbacks por defecto: <c>/signin-google</c> y <c>/signin-facebook</c>.
    /// </summary>
    private static void AddExternalLogins(this IServiceCollection services, IConfiguration configuration)
    {
        var authentication = services.AddAuthentication();

        var google = configuration.GetSection("Authentication:Google");
        if (!string.IsNullOrEmpty(google["ClientId"]) && !string.IsNullOrEmpty(google["ClientSecret"]))
        {
            authentication.AddGoogle(options =>
            {
                options.ClientId = google["ClientId"]!;
                options.ClientSecret = google["ClientSecret"]!;
                // Sin esto, tras cerrar sesión Google regresa de inmediato con la misma cuenta. Así muestra
                // "Elige una cuenta" en cada login (útil en teléfonos compartidos); no pide la contraseña.
                options.Events.OnRedirectToAuthorizationEndpoint = ctx =>
                {
                    ctx.Response.Redirect(QueryHelpers.AddQueryString(ctx.RedirectUri, "prompt", "select_account"));
                    return Task.CompletedTask;
                };
                ConfigureRemoteErrors(options);
            });
        }

        var facebook = configuration.GetSection("Authentication:Facebook");
        if (!string.IsNullOrEmpty(facebook["AppId"]) && !string.IsNullOrEmpty(facebook["AppSecret"]))
        {
            authentication.AddFacebook(options =>
            {
                options.AppId = facebook["AppId"]!;
                options.AppSecret = facebook["AppSecret"]!;
                options.Fields.Add("email");
                ConfigureRemoteErrors(options);
            });
        }
    }

    /// <summary>
    /// Si el usuario cancela en el proveedor o el intercambio falla, se regresa al callback de la página
    /// que inició el flujo con <c>remoteError</c> (en lugar de la página de error genérica).
    /// </summary>
    private static void ConfigureRemoteErrors(OAuthOptions options)
    {
        options.Events.OnAccessDenied = ctx =>
        {
            ctx.Response.Redirect(CallbackWithError(ctx.Properties?.RedirectUri, ExternalLoginErrors.Cancelled));
            ctx.HandleResponse();
            return Task.CompletedTask;
        };
        options.Events.OnRemoteFailure = ctx =>
        {
            ctx.Response.Redirect(CallbackWithError(ctx.Properties?.RedirectUri, ExternalLoginErrors.Failed));
            ctx.HandleResponse();
            return Task.CompletedTask;
        };
    }

    private static string CallbackWithError(string? redirectUri, string error)
    {
        // RedirectUri lo arma la propia página (ruta local); si falta o no es local se usa el callback de login.
        var callback = !string.IsNullOrEmpty(redirectUri) && redirectUri.StartsWith('/') && !redirectUri.StartsWith("//")
            ? redirectUri
            : "/Identity/Account/ExternalLogin?handler=Callback";
        return QueryHelpers.AddQueryString(callback, "remoteError", error);
    }

    private static Task RedirectOrStatus(RedirectContext<CookieAuthenticationOptions> ctx, int statusCode)
    {
        // /api y /hubs (SignalR) responden con el código; el resto del sitio redirige al login.
        if (ctx.Request.Path.StartsWithSegments("/api") || ctx.Request.Path.StartsWithSegments("/hubs"))
        {
            ctx.Response.StatusCode = statusCode;
        }
        else
        {
            ctx.Response.Redirect(ctx.RedirectUri + "&prompt=select_account");
        }
        return Task.CompletedTask;
    }
}

/// <summary>Valores de <c>remoteError</c> que entienden los callbacks de ExternalLogin y Manage/ExternalLogins.</summary>
public static class ExternalLoginErrors
{
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";

    /// <summary>Mensaje en español para el usuario (cualquier otro valor se trata como falla).</summary>
    public static string Message(string remoteError, string? provider = null)
    {
        var name = string.IsNullOrEmpty(provider) ? "el proveedor externo" : provider;
        return remoteError == Cancelled
            ? $"Cancelaste el inicio de sesión con {name}."
            : $"No se pudo iniciar sesión con {name}. Intenta de nuevo.";
    }
}
