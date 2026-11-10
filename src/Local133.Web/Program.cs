using Local133.Application;
using Local133.Infrastructure;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, logger) => logger
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.AddProblemDetails(options =>
        options.CustomizeProblemDetails = ctx =>
            ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier);

    builder.Services.AddControllersWithViews();
    // Razor Pages: las usa la UI de Identity scaffoldeada (Areas/Identity, paso 1.2).
    builder.Services.AddRazorPages();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    var app = builder.Build();

    // /api responde ProblemDetails; el resto del sitio, vistas HTML de error.
    app.UseWhen(ctx => IsApiRequest(ctx.Request), api =>
    {
        api.UseExceptionHandler();
        api.UseStatusCodePages();
    });
    app.UseWhen(ctx => !IsApiRequest(ctx.Request), site =>
    {
        site.UseExceptionHandler("/error");
        site.UseStatusCodePagesWithReExecute("/error/{0}");
    });

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }
    else
    {
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseRouting();
    app.UseAuthorization();

    app.MapStaticAssets();
    // Todos los controladores (API y vistas) usan rutas por atributo; las URLs del sitio van en español.
    app.MapControllers().WithStaticAssets();
    app.MapRazorPages().WithStaticAssets();

    // Rutas /api inexistentes: 404 ProblemDetails. Las demás rutas sin coincidencia caen en /error/404.
    app.MapFallback("api/{**slug}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound));

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "La aplicación terminó de forma inesperada");
}
finally
{
    Log.CloseAndFlush();
}

static bool IsApiRequest(HttpRequest request) => request.Path.StartsWithSegments("/api");

public partial class Program;
