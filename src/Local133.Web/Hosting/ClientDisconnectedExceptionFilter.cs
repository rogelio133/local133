using System.Data.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Local133.Web.Hosting;

/// <summary>
/// Petición abandonada por el cliente (otra búsqueda, otra navegación, pestaña cerrada) mientras la acción esperaba a
/// la base. El token de la petición se cancela y SqlClient lanza <c>SqlException</c> («Operation cancelled by user»)
/// o EF lanza <see cref="OperationCanceledException"/>. Sin este filtro se reportaba como 500 (log <c>ERR</c> y
/// re-ejecución de <c>/error</c>) para una respuesta que nadie iba a leer. Responde 499 (convención de nginx) sin
/// página de error y lo deja en <c>Debug</c>.
/// </summary>
public sealed class ClientDisconnectedExceptionFilter(ILogger<ClientDisconnectedExceptionFilter> logger) : IExceptionFilter
{
    public const int StatusCode = 499;

    public void OnException(ExceptionContext context)
    {
        var http = context.HttpContext;
        if (!http.RequestAborted.IsCancellationRequested || !IsCancellation(context.Exception))
        {
            return;
        }

        logger.LogDebug("El cliente abandonó {Method} {Path}", http.Request.Method, http.Request.Path);
        if (http.Features.Get<IStatusCodePagesFeature>() is { } statusCodePages)
        {
            statusCodePages.Enabled = false;
        }

        context.Result = new StatusCodeResult(StatusCode);
        context.ExceptionHandled = true;
    }

    private static bool IsCancellation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException or DbException)
            {
                return true;
            }
        }

        return false;
    }
}
