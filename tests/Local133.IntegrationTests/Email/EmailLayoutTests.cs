using System.Net;
using System.Text.RegularExpressions;
using Local133.Infrastructure.Email;
using Local133.IntegrationTests.Infrastructure;
using Local133.Web.Identity;

namespace Local133.IntegrationTests.Email;

/// <summary>Layout de los correos y plantillas. Sin SQL Server ni red: corre en CI.</summary>
public class EmailLayoutTests
{
    private const string Site = "https://local133.test";

    private static EmailContent Content(string actionUrl = Site + "/accion?a=1&b=2") => new(
        "Asunto <de> prueba",
        "Resumen",
        "Título & más",
        ["Primer párrafo con <script>alert(1)</script>", "Segundo"],
        "Hacer algo",
        actionUrl,
        "Pie \"citado\"");

    [Fact]
    public void El_boton_es_la_primera_liga_y_se_decodifica_completa()
    {
        var html = EmailLayout.Render(Content(), Site);

        Assert.Equal(Site + "/accion?a=1&b=2", new SentEmail("a@b.mx", "s", html).Link);
        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Codifica_todos_los_textos_y_deja_los_acentos_legibles()
    {
        var html = EmailLayout.Render(Content(), Site);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("Título &amp; más", html);
        Assert.Contains("Asunto &lt;de&gt; prueba", html);
        Assert.Contains("Primer párrafo", html);
    }

    [Theory]
    [InlineData("https://local133.test")]
    [InlineData("https://local133.test/")]
    public void El_logo_es_un_PNG_absoluto_con_medidas_y_alt(string site)
    {
        var html = EmailLayout.Render(Content(), site);

        Assert.Contains($"src=\"{Site}{EmailLayout.LogoPath}?v={EmailLayout.LogoVersion}\"", html);
        Assert.Contains($"width=\"{EmailLayout.LogoWidth}\" height=\"{EmailLayout.LogoHeight}\" alt=\"Local133\"", html);
        Assert.DoesNotContain(".svg", html);
    }

    [Fact]
    public void El_logo_existe_en_wwwroot()
    {
        var path = Path.Combine(RepoRoot(), "src", "Local133.Web", "wwwroot", EmailLayout.LogoPath.TrimStart('/'));
        var bytes = File.ReadAllBytes(path);

        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], bytes[..4]);
        Assert.True(bytes.Length < 20_000, $"El logo pesa {bytes.Length} bytes.");
    }

    [Fact]
    public void Correo_de_cuenta_activa_saluda_al_negocio_enmascara_la_CLABE_y_lleva_a_cobrar()
    {
        var html = MerchantEmails.BankAccountActiveBody("Tacos <Don> Ramón", "•••• 5713", "http://localhost:5231/");
        var decoded = WebUtility.HtmlDecode(html);

        Assert.Equal("http://localhost:5231/app/cobrar", new SentEmail("a@b.mx", "s", html).Link);
        Assert.Contains("¡Hola, Tacos <Don> Ramón!", decoded);
        Assert.DoesNotContain("Tacos <Don>", html);
        Assert.Contains("Tu cuenta bancaria •••• 5713 ya está activa en CoDi.", decoded);
        Assert.Contains("¡Ya puedes cobrar!", decoded);
        Assert.Contains("Empezar a cobrar", decoded);
    }

    [Fact]
    public void Correo_de_cuenta_activa_sin_nombre_de_negocio_saluda_sin_nombre() =>
        Assert.Contains("¡Hola!", WebUtility.HtmlDecode(MerchantEmails.BankAccountActiveBody(null, "•••• 5713", Site)));

    [Fact]
    public void Los_correos_de_la_cuenta_usan_el_layout_con_el_logo_del_host_de_la_liga()
    {
        const string callback = "https://localhost:7180/Identity/Account/ConfirmEmail?userId=1&code=abc";

        var html = AccountEmails.ConfirmBody(callback);

        Assert.Equal(callback, new SentEmail("a@b.mx", "s", html).Link);
        Assert.Contains($"src=\"https://localhost:7180{EmailLayout.LogoPath}", html);
        Assert.Contains("Confirmar mi correo", html);
        Assert.StartsWith("<!DOCTYPE html>", html);
    }

    [Theory]
    [InlineData(" https://local133.com.mx/ ", true, "https://local133.com.mx")]
    [InlineData("https://local133.com.mx", false, "https://local133.com.mx")]
    [InlineData("", true, SiteUrlOptions.DevelopmentFallbackBaseUrl)]
    [InlineData(null, true, SiteUrlOptions.DevelopmentFallbackBaseUrl)]
    [InlineData("", false, null)]
    [InlineData(null, false, null)]
    public void La_base_de_las_ligas_sale_de_PublicBaseUrl_y_solo_en_desarrollo_tiene_respaldo(
        string? configured, bool isDevelopment, string? expected) =>
        Assert.Equal(expected, new SiteUrlOptions { PublicBaseUrl = configured }.BaseUrl(isDevelopment));

    [Fact]
    public void FileEmailSender_pone_la_franja_dentro_del_documento_sin_anidar_otro()
    {
        var html = EmailLayout.Render(Content(), Site);

        var document = FileEmailSender.Document("a@b.mx", "Asunto", html);

        Assert.Single(Regex.Matches(document, "<!DOCTYPE", RegexOptions.IgnoreCase));
        Assert.Single(Regex.Matches(document, "<body", RegexOptions.IgnoreCase));
        Assert.Matches("<body[^>]*><p[^>]*><strong>Para:</strong> a@b.mx", document);
    }

    [Fact]
    public void FileEmailSender_envuelve_un_fragmento()
    {
        var document = FileEmailSender.Document("a@b.mx", "Asunto", "<p>Hola</p>");

        Assert.StartsWith("<!DOCTYPE html>", document);
        Assert.Contains("<p>Hola</p>", document);
        Assert.Contains("<strong>Para:</strong> a@b.mx", document);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Local133.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("No se encontró Local133.sln.");
    }
}
