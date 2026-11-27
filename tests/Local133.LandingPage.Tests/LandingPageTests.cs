extern alias DomainAssembly;

using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using DomainAssembly::Local133.Domain.Settings;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Local133.LandingPage.Tests;

public class LandingPageTests
{
    /// <summary>
    /// Días de prueba que dicen los textos publicados. <c>SystemSettings.DefaultTrialDays</c> ya es 15 (5.12); la portada
    /// y los Términos se actualizan en la 5.18, y entonces esta constante se quita.
    /// </summary>
    private const int PublishedTrialDays = 30;

    private const string AppUrl = "https://app.example.test";

    private static WebApplicationFactory<Program> CreateFactory(params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            // Sin watcher de Sass: cada factory levantaría su propio `sass --watch`.
            builder.UseSetting("Sass:Watch", "false");
            builder.UseSetting("Landing:AppUrl", AppUrl);
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);
        });

    private static async Task<string> GetHomeAsync(string query = "", params (string Key, string Value)[] settings)
    {
        using var factory = CreateFactory(settings);
        return await factory.CreateClient().GetStringAsync("/" + query);
    }

    /// <summary>Texto de la página sin etiquetas, con entidades decodificadas y espacios juntados.</summary>
    private static string TextOf(string html) =>
        Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]*?>", " ")), @"\s+", " ");

    private static IEnumerable<string> HrefsOf(string html, string linkText) =>
        Regex.Matches(html, $"href=\"(?<href>[^\"]*)\"[^>]*>{Regex.Escape(linkText)}</a>")
            .Select(match => match.Groups["href"].Value);

    [Fact]
    public async Task Portada_muestra_las_secciones_en_orden()
    {
        var html = await GetHomeAsync();

        string[] ids = ["inicio", "como-funciona", "funciones", "tu-dinero", "precios", "preguntas", "registro"];
        var positions = ids.Select(id => html.IndexOf($"id=\"{id}\"", StringComparison.Ordinal)).ToList();

        Assert.All(positions, position => Assert.True(position >= 0));
        Assert.Equal(positions.Order(), positions);
        Assert.True(html.IndexOf("Hecho para:", StringComparison.Ordinal) < positions[1]);
        Assert.True(html.IndexOf("Invita y gana", StringComparison.Ordinal) < positions[5]);
        Assert.True(html.IndexOf("<footer", StringComparison.Ordinal) > positions[6]);
    }

    [Fact]
    public async Task Portada_muestra_los_textos_principales()
    {
        var text = TextOf(await GetHomeAsync());

        Assert.Contains("sin terminal ni efectivo.", text);
        Assert.Contains("Escribe el monto y muestra el QR, o manda el cobro al celular de tu cliente.", text);
        Assert.Contains("Empieza a cobrar en tres pasos", text);
        Assert.Contains("Todo para cobrar y vender desde tu celular", text);
        Assert.Contains("nunca toca tu dinero", text);
        Assert.Contains("Prueba todo gratis 30 días", text);
        Assert.Contains("¿Tienes dudas?", text);
        Assert.Contains("listo para cobrar con CoDi", text);
        Assert.Contains("© 2026 Local133. CoDi® es una marca del Banco de México.", text);
        Assert.Equal(7, Regex.Matches(await GetHomeAsync(), "<details").Count);
    }

    [Fact]
    public async Task Cada_CoDi_lleva_la_marca_registrada()
    {
        var text = TextOf(await GetHomeAsync());

        Assert.Contains("CoDi", text);
        // En los títulos el ® va en un <sup>: sin etiquetas puede quedar separado por un espacio.
        Assert.DoesNotMatch(@"CoDi(?!\s*®)", text);
    }

    [Theory]
    [InlineData("?codigo=ABC234", AppUrl + "/r/ABC234")]
    [InlineData("?codigo=abc-234", AppUrl + "/r/ABC234")]
    [InlineData("?codigo=ABC10I", AppUrl)]
    [InlineData("?codigo=%3Cscript%3E", AppUrl)]
    [InlineData("?codigo=", AppUrl)]
    [InlineData("", AppUrl)]
    public async Task Los_botones_de_registro_llevan_el_codigo_de_invitacion(string query, string expectedHref)
    {
        var html = await GetHomeAsync(query);

        string[] signUpTexts =
        [
            "Prueba 30 días gratis", "Crear mi cuenta gratis", "Crear cuenta gratis", "Empezar con 30 días gratis",
        ];
        var hrefs = signUpTexts.SelectMany(text => HrefsOf(html, text)).ToList();

        // Encabezado y menú (2), hero y cierre (2), gratuito (1), Pro mensual y anual (2).
        Assert.Equal(7, hrefs.Count);
        Assert.All(hrefs, href => Assert.Equal(expectedHref, href));
    }

    [Fact]
    public async Task Iniciar_sesion_va_al_portal_y_las_ligas_legales_a_la_portada_sin_el_codigo()
    {
        var html = await GetHomeAsync("?codigo=ABC234");

        var login = HrefsOf(html, "Iniciar sesión").ToList();
        Assert.Equal(2, login.Count);
        Assert.All(login, href => Assert.Equal(AppUrl + "/Identity/Account/Login", href));
        // Los textos legales viven en la portada desde el 2026-11-25 (LegalController).
        Assert.Equal(new[] { "/terminos" }, HrefsOf(html, "Términos y condiciones"));
        Assert.Equal(new[] { "/aviso-de-privacidad" }, HrefsOf(html, "Aviso de privacidad"));
    }

    [Fact]
    public async Task Las_ligas_al_portal_abren_en_la_misma_pestana()
    {
        var html = await GetHomeAsync();

        Assert.DoesNotContain("target=\"_blank\"", html);
    }

    [Fact]
    public async Task Hero_cierre_y_hecho_para_salen_de_las_palabras_configuradas()
    {
        var html = await GetHomeAsync("",
            ("Home:RotatingWords:0", "puesto"),
            ("Home:RotatingWords:1", "local"),
            ("Home:RotatingWords:2", "consultorio"),
            ("Home:RotatingWords:3", ""),
            ("Home:RotatingWords:4", ""),
            ("Home:RotatingWords:5", ""));

        const string rotating =
            "data-rotating-words=\"[&quot;puesto&quot;,&quot;local&quot;,&quot;consultorio&quot;]\">puesto</span>";
        Assert.Contains("class=\"rotating-word hero-word\" " + rotating, html);
        Assert.Contains("class=\"rotating-word closing-rotating-word\" " + rotating, html);
        Assert.Contains("data-longest=\"consultorio\"", html);

        var labels = Regex.Matches(html, "<span class=\"made-for-label\">(?<label>[^<]*)</span>")
            .Select(match => match.Groups["label"].Value);
        Assert.Equal(new[] { "Puestos", "Locales", "Consultorios" }, labels);
        Assert.Contains("src=\"/js/rotating-word.js", html);
    }

    [Fact]
    public async Task Con_una_sola_palabra_no_carga_la_animacion()
    {
        var html = await GetHomeAsync("",
            ("Home:RotatingWords:0", "puesto"),
            ("Home:RotatingWords:1", ""),
            ("Home:RotatingWords:2", ""),
            ("Home:RotatingWords:3", ""),
            ("Home:RotatingWords:4", ""),
            ("Home:RotatingWords:5", ""));

        Assert.Contains("<span class=\"rotating-word hero-word\">puesto</span>", html);
        Assert.Contains("<span class=\"rotating-word closing-rotating-word\">puesto</span>", html);
        Assert.DoesNotContain("data-rotating-words", html);
        Assert.DoesNotContain("rotating-word.js", html);
        Assert.Contains("src=\"/js/landing.js", html);
    }

    [Fact]
    public async Task Los_precios_coinciden_con_los_de_SystemSettings()
    {
        // La portada no tiene base: los precios van escritos en _Pricing.cshtml. Si cambia un valor por defecto,
        // hay que cambiar también la vista (y, si el admin lo cambia en producción, la vista a mano).
        var text = TextOf(await GetHomeAsync());

        static string Money(decimal amount) =>
            "$" + amount.ToString(amount == decimal.Truncate(amount) ? "#,0" : "#,0.00", CultureInfo.InvariantCulture);

        var monthly = SystemSettings.DefaultProMonthlyPrice;
        var annual = SystemSettings.DefaultProAnnualPrice;
        Assert.Contains($"{Money(monthly)} al mes", text);
        Assert.Contains($"{Money(annual)} al año", text);
        Assert.Contains($"Equivale a {Money(decimal.Round(annual / 12, 2))} al mes", text);
        Assert.Contains($"Ahorras {Money(monthly * 12 - annual)} al año", text);
        Assert.Contains($"3 meses ({Money(monthly * 3)}) y 6 meses ({Money(monthly * 6)})", text);
        Assert.Contains($"los primeros {SystemSettings.DefaultFounderSlots} vendedores que paguen Pro pagan " +
            $"{Money(SystemSettings.DefaultProFounderMonthlyPrice)} al mes o {Money(SystemSettings.DefaultProFounderAnnualPrice)} al año", text);
        Assert.Contains($"{SystemSettings.DefaultFreeMonthlyPaidLimit} cobros al mes", text);
        Assert.Contains($"Historial de los últimos {SystemSettings.DefaultFreeHistoryDays} días", text);
        Assert.Contains($"Prueba todo gratis {PublishedTrialDays} días", text);
        Assert.Contains($"+{SystemSettings.DefaultReferralRewardDays} días de Pro para ti", text);
        Assert.Contains($"-{SystemSettings.DefaultReferralWelcomeDiscountRate * 100:0}% de descuento", text);
        Assert.Contains($"Tienes {SystemSettings.DefaultGraceDays} días de gracia", text);
    }

    [Fact]
    public async Task No_muestra_datos_pendientes()
    {
        var html = await GetHomeAsync();

        // Lugares de fundador, correo y WhatsApp de contacto: pendientes (log_plan/portada-nueva.md).
        Assert.DoesNotContain("Quedan", html);
        Assert.DoesNotContain("[N]", html);
        Assert.DoesNotContain("hola@notificaciones", html);
        Assert.DoesNotContain("Contacto", html);
    }

    [Fact]
    public async Task Las_secciones_aparecen_al_hacer_scroll_sin_ocultarse_sin_JS()
    {
        var html = await GetHomeAsync();

        // Títulos de sección, «Próximamente», tu dinero, fundador, Invita y gana, preguntas y cierre.
        Assert.Equal(10, Regex.Matches(html, @"\sdata-reveal[\s>]").Count);
        // Pasos, herramientas, tu dinero, precios e Invita y gana: sus tarjetas aparecen escalonadas.
        Assert.Equal(5, Regex.Matches(html, @"\sdata-reveal-stagger[\s>]").Count);
        Assert.Contains("data-carousel-track data-reveal-stagger>", html);

        // El hero se ve al cargar: nunca se anima.
        var heroStart = html.IndexOf("id=\"inicio\"", StringComparison.Ordinal);
        var hero = html[heroStart..html.IndexOf("</section>", heroStart, StringComparison.Ordinal)];
        Assert.DoesNotContain("data-reveal", hero);

        // El estado oculto lo pone landing.js (.reveal-ready): sin JS todo se ve.
        Assert.DoesNotContain("reveal-ready", html);
        Assert.DoesNotContain("is-revealed", html);
    }

    [Fact]
    public async Task Menu_del_celular_es_accesible()
    {
        var html = await GetHomeAsync();

        Assert.Contains("aria-label=\"Abrir menú\" aria-expanded=\"false\" aria-controls=\"menu-movil\"", html);
        Assert.Contains("id=\"menu-movil\"", html);
    }

    [Theory]
    [InlineData("/icon-192.png")]
    [InlineData("/favicon.ico")]
    [InlineData("/apple-touch-icon.png")]
    [InlineData("/js/rotating-word.js")]
    [InlineData("/img/local133-logo-48.png")]
    [InlineData("/img/local133-logo-96.png")]
    [InlineData("/img/local133-logo-144.png")]
    [InlineData("/img/local133-logo-160.png")]
    [InlineData("/img/local133-logo-320.png")]
    [InlineData("/img/local133-logo-480.png")]
    [InlineData("/img/toldo-patron.svg")]
    [InlineData("/js/landing.js")]
    [InlineData("/js/section-index.js")]
    [InlineData("/css/landing.css")]
    public async Task Sirve_los_archivos_de_la_portada(string path)
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
