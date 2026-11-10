using Local133.Infrastructure.Identity;
using Local133.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace Local133.Web.Identity;

public static class IdentitySetup
{
    /// <summary>
    /// Identity con cookie y roles. Se usa <c>AddIdentity</c> (no <c>AddDefaultIdentity</c>/<c>AddDefaultUI</c>):
    /// solo existen las páginas scaffoldeadas en Areas/Identity; las demás (2FA, datos personales) dan 404.
    /// </summary>
    public static IServiceCollection AddAppIdentity(this IServiceCollection services, IWebHostEnvironment environment)
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
            .AddErrorDescriber<SpanishIdentityErrorDescriber>();

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

        return services;
    }

    private static Task RedirectOrStatus(RedirectContext<CookieAuthenticationOptions> ctx, int statusCode)
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.StatusCode = statusCode;
        }
        else
        {
            ctx.Response.Redirect(ctx.RedirectUri);
        }
        return Task.CompletedTask;
    }
}
