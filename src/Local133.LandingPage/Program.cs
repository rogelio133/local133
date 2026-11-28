using Local133.LandingPage.Hosting;
using Local133.LandingPage.Services;
using Local133.Web.Hosting;
using Local133.Web.PublicPages;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Detrás del front-end de App Service (lo mismo que ASPNETCORE_FORWARDEDHEADERS_ENABLED=true, pero en código para no
// depender de un App Setting): el esquema original (https) para UseHttpsRedirection y HSTS, y la IP del cliente para el
// límite por IP del recibo y del catálogo. Sin redes conocidas, porque App Service no expone la IP del front-end; con
// ForwardLimit = 1 solo se toma el último valor de X-Forwarded-For, el que agrega el front-end, así que lo que el
// cliente escriba antes en ese encabezado no cuenta. El sitio solo es accesible a través de ese front-end.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Palabras que rotan en el título (Home:RotatingWords, mismas reglas que Local133.Web) y destino del botón.
builder.Services.Configure<HomeHeroOptions>(builder.Configuration.GetSection(HomeHeroOptions.SectionName));
builder.Services.Configure<LandingOptions>(builder.Configuration.GetSection(LandingOptions.SectionName));

// Recibo y catálogo públicos (2026-11-27): sus datos salen de la API pública del portal (Landing:AppUrl), que solo
// acepta la llave compartida PublicApi:Key (secreto: user-secrets / App Settings). Sin ella el portal responde 401 y
// estas páginas dicen «No pudimos cargar…».
if (string.IsNullOrWhiteSpace(builder.Configuration[PublicPagesApi.KeyConfigKey]))
{
    Console.Error.WriteLine($"Aviso: falta {PublicPagesApi.KeyConfigKey}; el recibo y el catálogo públicos no podrán cargar.");
}

builder.Services.AddHttpClient<PublicPagesClient>((services, http) =>
    {
        var appUrl = services.GetRequiredService<IOptions<LandingOptions>>().Value.AppUrl;
        http.BaseAddress = new Uri(string.IsNullOrWhiteSpace(appUrl) ? LandingOptions.DefaultAppUrl : appUrl.Trim().TrimEnd('/') + "/");
        var key = services.GetRequiredService<IConfiguration>()[PublicPagesApi.KeyConfigKey];
        if (!string.IsNullOrWhiteSpace(key))
        {
            http.DefaultRequestHeaders.Add(PublicPagesApi.KeyHeader, key.Trim());
        }
    })
    .AddStandardResilienceHandler();

// Límite por IP de esas dos páginas (RateLimiting:*): cada visita es una llamada al portal.
builder.Services.AddPublicPagesRateLimit(builder.Configuration);

#if DEBUG
// Estilos: Styles/landing.scss → wwwroot/css/landing.css. El build y el publish compilan con dart-sass
// (AspNetCore.SassCompiler); en Debug además se recompila al guardar. Sass:Watch=false lo apaga (pruebas).
if (builder.Configuration.GetValue("Sass:Watch", defaultValue: true))
    builder.Services.AddSassCompiler();
#endif

var app = builder.Build();

// Primero: la redirección a HTTPS, HSTS y el límite por IP deben ver el esquema y la IP originales.
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStatusCodePages();
app.UseRouting();
app.UseRateLimiter();
app.MapStaticAssets();
app.MapControllers();

app.Run();

/// <summary>Visible para <c>WebApplicationFactory</c> en las pruebas.</summary>
public partial class Program;
