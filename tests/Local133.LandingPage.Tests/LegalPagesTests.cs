extern alias DomainAssembly;

using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using DomainAssembly::Local133.Domain.Settings;
using Local133.Domain.Legal;
using Local133.Web.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Local133.LandingPage.Tests;

/// <summary>
/// 5.11: Términos y Aviso de privacidad, en la portada desde el 2026-11-25 (LegalController). Los números van escritos
/// en el texto (documento versionado); estas pruebas fallan si dejan de coincidir con los valores por defecto de
/// <see cref="SystemSettings"/> o con <see cref="ReferralCookie"/>. Al corregirlos, subir la versión en
/// <see cref="LegalDocuments"/> (enlazado desde Local133.Domain).
/// </summary>
public partial class LegalPagesTests
{
    private const string AppUrl = "https://app.example.test";

    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    private static async Task<string> HtmlAsync(string url)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Sass:Watch", "false");
            builder.UseSetting("Landing:AppUrl", AppUrl);
        });
        return await factory.CreateClient().GetStringAsync(url);
    }

    [Theory]
    [InlineData("/terminos", "Términos y condiciones", LegalDocuments.CurrentTermsVersion)]
    [InlineData("/aviso-de-privacidad", "Aviso de privacidad", LegalDocuments.CurrentPrivacyNoticeVersion)]
    public async Task Paginas_legales_son_publicas_y_muestran_su_version(string url, string title, string version)
    {
        // La portada codifica los acentos de lo que sale de @ (Términos ⇒ T&#xE9;rminos): se compara decodificado.
        var html = WebUtility.HtmlDecode(await HtmlAsync(url));

        Assert.Contains($"<h1 class=\"legal-title\">{title}</h1>", html);
        Assert.Contains($"<title>{title} · Local133</title>", html);
        Assert.Contains($"Versión {version}", html);
        Assert.Contains("Borrador pendiente de revisión legal.", html);
    }

    [Theory]
    [InlineData("/terminos")]
    [InlineData("/aviso-de-privacidad")]
    public async Task Llevan_el_encabezado_el_pie_y_la_tipografia_de_la_portada(string url)
    {
        var html = await HtmlAsync(url);

        Assert.Contains("<header class=\"site-header\">", html);
        Assert.Contains("<footer class=\"site-footer\">", html);
        Assert.Contains("family=Baloo+2", html);
        Assert.Contains("src=\"/js/landing.js", html);

        // Las secciones del menú y del pie regresan a la portada; el logo, a su inicio.
        Assert.Contains("href=\"/#como-funciona\"", html);
        Assert.Contains("href=\"/#precios\"", html);
        Assert.Contains("<a class=\"brand\" href=\"/\">", html);
        Assert.DoesNotContain("href=\"#inicio\"", html);
    }

    [Fact]
    public async Task Registro_e_inicio_de_sesion_van_al_portal_y_las_ligas_legales_se_quedan_en_la_portada()
    {
        var html = await HtmlAsync("/terminos");

        Assert.Equal(2, Regex.Matches(html, $"href=\"{Regex.Escape(AppUrl)}\"[^>]*>Prueba 30 días gratis</a>").Count);
        Assert.Equal(2, Regex.Matches(html, $"href=\"{Regex.Escape(AppUrl)}/Identity/Account/Login\"").Count);
        Assert.Contains("href=\"/terminos\">Términos y condiciones</a>", html);
        Assert.Contains("href=\"/aviso-de-privacidad\">Aviso de privacidad</a>", html);
        Assert.DoesNotContain("codigo", html);
    }

    [Fact]
    public async Task Los_terminos_traen_los_numeros_vigentes_de_la_suscripcion()
    {
        var text = await TextAsync("/terminos");

        string[] expected =
        [
            $"{Money(SystemSettings.DefaultMonthlyPrice)} al mes",
            $"{Money(SystemSettings.DefaultAnnualPrice)} al año",
            $"{Money(SystemSettings.DefaultFounderMonthlyPrice)} al mes",
            $"{Money(SystemSettings.DefaultFounderAnnualPrice)} al año",
            $"primeros {Count(SystemSettings.DefaultFounderSlots)} vendedores",
            $"{Count(SystemSettings.DefaultTrialDays)} días de prueba",
            $"{Count(SystemSettings.DefaultGraceDays)} días de gracia",
            $"{Count(SystemSettings.DefaultFreeMonthlyPaidLimit)} cobros concretados por mes",
            $"{Count(SystemSettings.DefaultFreeMonthlyGeneratedLimit)} cobros generados",
            $"últimos {Count(SystemSettings.DefaultFreeHistoryDays)} días",
            $"{Count(SystemSettings.DefaultProFairUseMonthlyPaid)} cobros concretados al mes",
            $"{Count(SystemSettings.DefaultReferralRewardDays)} días de Pro cuando",
            $"{Count(SystemSettings.DefaultReferralRewardsPerYear)} premios en 12 meses",
            $"{Count((int)(SystemSettings.DefaultReferralWelcomeDiscountRate * 100))} % de descuento",
            $"por {Count((int)ReferralCookie.Lifetime.TotalDays)} días",
        ];

        Assert.All(expected, phrase => Assert.Contains(phrase, text));
    }

    [Fact]
    public async Task El_aviso_de_privacidad_explica_la_cookie_de_invitacion_y_la_conservacion()
    {
        var text = await TextAsync("/aviso-de-privacidad");

        Assert.Contains($"{ReferralCookie.Name}, que guarda por {Count((int)ReferralCookie.Lifetime.TotalDays)} días", text);
        Assert.Contains("2 años sin actividad", text);
        Assert.Contains("solo tu primer nombre", text);
        Assert.Contains("resumen mensual", text);
    }

    [Theory]
    [InlineData("/terminos")]
    [InlineData("/aviso-de-privacidad")]
    public async Task Cada_liga_del_indice_lleva_a_una_seccion(string url)
    {
        var html = await HtmlAsync(url);

        var anchors = AnchorLink().Matches(html).Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(anchors);
        Assert.All(anchors, id => Assert.Contains($"id=\"{id}\"", html));

        // section-index.js pasa el foco al título: sin él, Chrome y Edge regresaban al índice tras el salto.
        Assert.Contains("<nav aria-label=\"Contenido\" class=\"small\" data-section-index>", html);
        Assert.Contains("src=\"/js/section-index.", html);
    }

    [Theory]
    [InlineData("/terminos")]
    [InlineData("/aviso-de-privacidad")]
    public async Task Los_datos_del_responsable_siguen_marcados_como_pendientes(string url)
    {
        var html = await HtmlAsync(url);

        Assert.Contains("<mark>[correo de contacto]</mark>", html);
        Assert.Contains("<mark>[razón social y domicilio]</mark>", html);
    }

    /// <summary>Texto visible de la página, sin etiquetas y con los espacios juntos.</summary>
    private static async Task<string> TextAsync(string url)
    {
        var html = await HtmlAsync(url);
        return Spaces().Replace(Tags().Replace(html, ""), " ");
    }

    private static string Money(decimal amount) => amount.ToString("$#,0", Mx);

    private static string Count(int n) => n.ToString("#,0", Mx);

    // Solo el índice: las anclas de las secciones de la portada empiezan con «/#».
    [GeneratedRegex("href=\"#([a-z-]+)\"")]
    private static partial Regex AnchorLink();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
