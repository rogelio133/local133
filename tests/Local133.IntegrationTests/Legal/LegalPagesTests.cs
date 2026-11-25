using System.Globalization;
using System.Text.RegularExpressions;
using Local133.Domain.Legal;
using Local133.Domain.Settings;
using Local133.IntegrationTests.Infrastructure;
using Local133.Web.Identity;
using static Local133.IntegrationTests.Identity.ExternalLoginTests;

namespace Local133.IntegrationTests.Legal;

/// <summary>
/// 5.11: Términos y Aviso de privacidad. Los números van escritos en el texto (documento versionado); estas pruebas
/// fallan si dejan de coincidir con los valores por defecto de <see cref="SystemSettings"/>. Al corregirlos, subir la
/// versión en <see cref="LegalDocuments"/>.
/// </summary>
public partial class LegalPagesTests(Local133WebFactory factory) : WebTestBase(factory)
{
    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    [Theory]
    [InlineData("/terminos", "Términos y condiciones", LegalDocuments.CurrentTermsVersion)]
    [InlineData("/aviso-de-privacidad", "Aviso de privacidad", LegalDocuments.CurrentPrivacyNoticeVersion)]
    public async Task Paginas_legales_son_publicas_y_muestran_su_version(string url, string title, string version)
    {
        var html = await HtmlAsync(CreateClient(), url);

        Assert.Contains($"<h1 class=\"h3\">{title}</h1>", html);
        Assert.Contains($"Versión {version}", html);
        Assert.Contains("Borrador pendiente de revisión legal.", html);
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
        var html = await HtmlAsync(CreateClient(), url);

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
        var html = await HtmlAsync(CreateClient(), url);

        Assert.Contains("<mark>[correo de contacto]</mark>", html);
        Assert.Contains("<mark>[razón social y domicilio]</mark>", html);
    }

    /// <summary>Texto visible de la página, sin etiquetas y con los espacios juntos.</summary>
    private async Task<string> TextAsync(string url)
    {
        var html = await HtmlAsync(CreateClient(), url);
        return Spaces().Replace(Tags().Replace(html, ""), " ");
    }

    private static string Money(decimal amount) => amount.ToString("$#,0", Mx);

    private static string Count(int n) => n.ToString("#,0", Mx);

    [GeneratedRegex("href=\"#([a-z-]+)\"")]
    private static partial Regex AnchorLink();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
