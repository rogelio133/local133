using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Local133.Web.PublicPages;

/// <summary>
/// Solo la portada usa <c>/api/public</c> (ajustes 2026-11-27): la llama su servidor, nunca el navegador, con la llave
/// compartida en <see cref="PublicPagesApi.KeyHeader"/>. Sin ella o con otra ⇒ 401. Si el portal no tiene
/// <see cref="PublicPagesApi.KeyConfigKey"/> ⇒ 500 (falla cerrada: la API no queda abierta por un olvido de
/// configuración). La comparación es de tiempo constante.
/// </summary>
public sealed class PublicApiKeyFilter(IConfiguration configuration, ILogger<PublicApiKeyFilter> logger) : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var expected = configuration[PublicPagesApi.KeyConfigKey];
        if (string.IsNullOrWhiteSpace(expected))
        {
            logger.LogError("Llegó una petición a /api/public pero falta {Key}; se responde 500", PublicPagesApi.KeyConfigKey);
            context.Result = Problem(context, StatusCodes.Status500InternalServerError);
            return;
        }

        var provided = context.HttpContext.Request.Headers[PublicPagesApi.KeyHeader].ToString();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected.Trim())))
        {
            logger.LogWarning("Petición a {Path} sin la llave de la portada desde {RemoteIp}",
                context.HttpContext.Request.Path, context.HttpContext.Connection.RemoteIpAddress);
            context.Result = Problem(context, StatusCodes.Status401Unauthorized);
        }
    }

    private static ObjectResult Problem(AuthorizationFilterContext context, int statusCode)
    {
        var problem = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>()
            .CreateProblemDetails(context.HttpContext, statusCode);
        return new ObjectResult(problem) { StatusCode = statusCode, ContentTypes = { "application/problem+json" } };
    }
}
