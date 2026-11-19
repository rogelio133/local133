using Local133.LandingPage.Hosting;
using Local133.Web.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Palabras que rotan en el título (Home:RotatingWords, mismas reglas que Local133.Web) y destino del botón.
builder.Services.Configure<HomeHeroOptions>(builder.Configuration.GetSection(HomeHeroOptions.SectionName));
builder.Services.Configure<LandingOptions>(builder.Configuration.GetSection(LandingOptions.SectionName));

#if DEBUG
// Estilos: Styles/landing.scss → wwwroot/css/landing.css. El build y el publish compilan con dart-sass
// (AspNetCore.SassCompiler); en Debug además se recompila al guardar. Sass:Watch=false lo apaga (pruebas).
if (builder.Configuration.GetValue("Sass:Watch", defaultValue: true))
    builder.Services.AddSassCompiler();
#endif

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
    // Detrás del proxy de App Service requiere ASPNETCORE_FORWARDEDHEADERS_ENABLED=true (ver azure.md).
    app.UseHttpsRedirection();
}

app.UseStatusCodePages();
app.MapStaticAssets();
app.MapControllers();

app.Run();

/// <summary>Visible para <c>WebApplicationFactory</c> en las pruebas.</summary>
public partial class Program;
