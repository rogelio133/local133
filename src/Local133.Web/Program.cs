using Local133.Application;
using Local133.Infrastructure;
using Local133.Web.Identity;
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

    builder.Services.AddControllersWithViews(options => options.ModelBindingMessageProvider.UseSpanishMessages());
    // Razor Pages: las usa la UI de Identity scaffoldeada (Areas/Identity). Sin AddDefaultUI estas
    // convenciones no vienen solas: Manage y Logout requieren sesión.
    builder.Services.AddRazorPages(options =>
    {
        options.Conventions.AuthorizeAreaFolder("Identity", "/Account/Manage");
        options.Conventions.AuthorizeAreaPage("Identity", "/Account/Logout");
    });
    // Razor no escapa acentos ni ñ como entidades (&#xF3;): el HTML sale legible en español.
    builder.Services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(options =>
        options.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddAppIdentity(builder.Configuration, builder.Environment);

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
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapStaticAssets();
    // Todos los controladores (API y vistas) usan rutas por atributo; las URLs del sitio van en español.
    app.MapControllers().WithStaticAssets();
    app.MapRazorPages().WithStaticAssets();

    // Rutas /api inexistentes: 404 ProblemDetails. Las demás rutas sin coincidencia caen en /error/404.
    app.MapFallback("api/{**slug}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound));

    // Admin desde Seed:Admin:Email/Password (si están configurados); idempotente.
    await app.SeedAdminAsync();

    await app.RunAsync();
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
