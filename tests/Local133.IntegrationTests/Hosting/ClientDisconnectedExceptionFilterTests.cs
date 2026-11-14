using Local133.Web.Hosting;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Local133.IntegrationTests.Hosting;

/// <summary>
/// Una petición que el cliente abandonó (otra búsqueda, otra navegación) no se reporta como 500. Sin base ni sitio:
/// corre en CI.
/// </summary>
public class ClientDisconnectedExceptionFilterTests
{
    [Theory]
    [MemberData(nameof(CancellationExceptions))]
    public void Peticion_abandonada_responde_499_sin_pagina_de_error(Exception exception)
    {
        var (context, statusCodePages) = ContextFor(exception, aborted: true);

        Filter().OnException(context);

        Assert.True(context.ExceptionHandled);
        Assert.Equal(ClientDisconnectedExceptionFilter.StatusCode, Assert.IsType<StatusCodeResult>(context.Result).StatusCode);
        Assert.False(statusCodePages.Enabled);
    }

    [Theory]
    [MemberData(nameof(CancellationExceptions))]
    public void Sin_abandono_la_falla_sigue_su_curso(Exception exception)
    {
        var (context, statusCodePages) = ContextFor(exception, aborted: false);

        Filter().OnException(context);

        Assert.False(context.ExceptionHandled);
        Assert.Null(context.Result);
        Assert.True(statusCodePages.Enabled);
    }

    [Fact]
    public void Otra_excepcion_con_peticion_abandonada_no_se_oculta()
    {
        var (context, _) = ContextFor(new InvalidOperationException("bug"), aborted: true);

        Filter().OnException(context);

        Assert.False(context.ExceptionHandled);
    }

    public static TheoryData<Exception> CancellationExceptions() => new()
    {
        new OperationCanceledException(),
        new TaskCanceledException(),
        new FakeDbException(),
        new InvalidOperationException("envuelta", new FakeDbException()),
    };

    private static ClientDisconnectedExceptionFilter Filter() => new(NullLogger<ClientDisconnectedExceptionFilter>.Instance);

    private static (ExceptionContext Context, StatusCodePagesFeature StatusCodePages) ContextFor(Exception exception, bool aborted)
    {
        var http = new DefaultHttpContext { RequestAborted = new CancellationToken(aborted) };
        var statusCodePages = new StatusCodePagesFeature();
        http.Features.Set<IStatusCodePagesFeature>(statusCodePages);
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return (new ExceptionContext(action, []) { Exception = exception }, statusCodePages);
    }

    /// <summary>Como la <c>SqlException</c> «Operation cancelled by user» (sellada, sin constructor público).</summary>
    private sealed class FakeDbException() : System.Data.Common.DbException("Operation cancelled by user.");
}
