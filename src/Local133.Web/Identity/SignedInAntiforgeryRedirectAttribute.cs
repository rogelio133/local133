using Local133.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Core.Infrastructure;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Local133.Web.Identity;

/// <summary>
/// Doble envío de un formulario que inicia sesión (Enter + clic, doble clic o el administrador de contraseñas):
/// el primer POST deja la cookie de sesión y el segundo llega con un token antiforgery generado para el usuario
/// anónimo, así que falla con 400 y la persona ve «Algo salió mal» aunque ya entró (producción, 2026-11-26).
/// Si el token falla y la petición ya trae sesión, se redirige a su inicio (o al <c>returnUrl</c> local) en lugar
/// del 400. Sin sesión, el 400 se queda igual: el token sigue protegiendo el formulario.
/// </summary>
/// <remarks>
/// Es un <see cref="IAlwaysRunResultFilter"/> porque la validación antiforgery es un filtro de autorización:
/// cuando falla, corta la petición y solo los filtros de resultado «siempre» ven su resultado.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SignedInAntiforgeryRedirectAttribute : Attribute, IAsyncAlwaysRunResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (context.Result is IAntiforgeryValidationFailedResult && http.User.Identity?.IsAuthenticated == true)
        {
            var userManager = http.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
            var url = http.RequestServices.GetRequiredService<IUrlHelperFactory>().GetUrlHelper(context);
            var returnUrl = http.Request.Query["returnUrl"].ToString();

            var target = !string.IsNullOrEmpty(returnUrl) && url.IsLocalUrl(returnUrl)
                ? returnUrl
                : await PostLoginRedirect.HomeUrlAsync(userManager, await userManager.GetUserAsync(http.User));

            http.RequestServices.GetRequiredService<ILogger<SignedInAntiforgeryRedirectAttribute>>()
                .LogInformation("Formulario de {Path} enviado otra vez con la sesión ya iniciada; se redirige a su inicio.", http.Request.Path);
            context.Result = new LocalRedirectResult(target);
        }

        await next();
    }
}
