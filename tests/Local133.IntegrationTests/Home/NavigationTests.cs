using System.Net;
using System.Text.RegularExpressions;
using Local133.Application.Auth;
using Local133.IntegrationTests.Infrastructure;

namespace Local133.IntegrationTests.Home;

/// <summary>
/// Marca y navegación (ajustes 2026-11-26, puntos 1, 2, 4, 5 y 6): la home solo sin sesión, la barra según el rol y los
/// íconos del <c>&lt;head&gt;</c>.
/// </summary>
public partial class NavigationTests(Local133WebFactory factory) : WebTestBase(factory)
{
    private const string MerchantEmail = "vendedor@prueba.mx";
    private const string AdminEmail = "admin@prueba.mx";

    [Fact]
    public async Task Home_sin_sesion_va_sin_barra_con_el_logo_y_los_dos_botones()
    {
        var response = await CreateClient().GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("<nav class=\"navbar", html);
        Assert.Contains("class=\"d-flex flex-column site-layout home-page\"", html);
        Assert.Matches(@"class=""home-logo"" src=""/img/local133-logo-160[^""]*\.png""", html);
        Assert.Contains(">Crear cuenta</a>", html);
        Assert.Contains(">Entrar</a>", html);
        Assert.DoesNotContain("Ir a mi puesto", html);
    }

    [Fact]
    public async Task Home_con_sesion_de_vendedor_redirige_a_app()
    {
        await CreateMerchantAsync(MerchantEmail);
        var client = CreateClient();
        await LoginAsync(client, MerchantEmail);

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/app", Location(response));
    }

    [Fact]
    public async Task Home_con_sesion_de_admin_redirige_a_clientes()
    {
        await CreateConfirmedUserAsync(AdminEmail, Roles.Admin);
        var client = CreateClient();
        await LoginAsync(client, AdminEmail);

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/clientes", Location(response));
    }

    [Fact]
    public async Task Barra_del_vendedor_liga_a_mi_informacion_y_trae_la_marca()
    {
        await CreateMerchantAsync(MerchantEmail);
        var client = CreateClient();
        await LoginAsync(client, MerchantEmail);

        var app = await HtmlAsync(client, "/app");
        Assert.Contains("class=\"navbar-brand d-flex align-items-center gap-2\" href=\"/app\"", app);
        Assert.Contains("<span class=\"navbar-wordmark\">Local<span>133</span></span>", app);
        Assert.Contains("href=\"/app/mi-informacion\" title=\"Mi información\" data-navbar-profile", app);
        Assert.DoesNotContain("/Identity/Account/Manage\"", app);
        Assert.DoesNotContain("navbar-admin-badge", app);

        // Layout público (Identity) con sesión de vendedor: el logo lleva a /app y ya no hay «Mi puesto».
        var manage = await HtmlAsync(client, "/Identity/Account/Manage/ChangePassword");
        Assert.Contains("class=\"navbar-brand d-flex align-items-center gap-2\" href=\"/app\"", manage);
        Assert.Contains("navbar-wordmark", manage);
        Assert.DoesNotContain("Mi puesto", manage);
        // Menú de «Mi cuenta» (Manage/_ViewStart): desde Contraseña se llega a las cuentas externas.
        Assert.Contains("id=\"change-password\"", manage);
        Assert.Contains("href=\"/Identity/Account/Manage/ExternalLogins\"", manage);
    }

    [Fact]
    public async Task Barra_del_admin_muestra_el_correo_sin_liga_y_su_marca()
    {
        await CreateConfirmedUserAsync(AdminEmail, Roles.Admin);
        var client = CreateClient();
        await LoginAsync(client, AdminEmail);

        var html = await HtmlAsync(client, "/admin/clientes");

        Assert.Contains("href=\"/admin/clientes\"", html);
        Assert.Contains("<span class=\"navbar-wordmark-number\">133</span>", html);
        Assert.Contains("navbar-admin-badge d-none d-sm-inline-block\">Admin</span>", html);
        Assert.DoesNotContain("bi-person-circle", html);
        Assert.DoesNotContain("data-navbar-profile", html);
        Assert.Contains($"data-navbar-email>{AdminEmail}</span>", html);
        // «Mi cuenta» (contraseña) en el menú, porque la barra ya no lleva a Manage.
        Assert.Contains("href=\"/Identity/Account/Manage/ChangePassword\"", html);
        Assert.Contains("Mi cuenta", html);

        // En Identity, el logo del admin lleva a Clientes y no hay liga «Admin» aparte.
        var manage = await HtmlAsync(client, "/Identity/Account/Manage/ChangePassword");
        Assert.Contains("class=\"navbar-brand d-flex align-items-center gap-2\" href=\"/admin/clientes\"", manage);
        Assert.DoesNotContain("href=\"/admin\"", manage);
        Assert.DoesNotContain("data-manage-profile-link", await HtmlAsync(client, "/Identity/Account/Manage"));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Identity/Account/Login")]
    public async Task Los_archivos_del_head_existen(string page)
    {
        var client = CreateClient();
        var html = await HtmlAsync(client, page);
        var head = html[..html.IndexOf("</head>", StringComparison.Ordinal)];

        var paths = LocalAssetRegex().Matches(head).Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).Distinct().ToList();

        // MapStaticAssets pone huella en las URL (/favicon.tp74xsnl14.ico).
        Assert.Contains(paths, p => p.StartsWith("/favicon.", StringComparison.Ordinal) && p.EndsWith(".ico", StringComparison.Ordinal));
        Assert.Contains(paths, p => p.StartsWith("/icon-192.", StringComparison.Ordinal));
        Assert.Contains(paths, p => p.StartsWith("/apple-touch-icon.", StringComparison.Ordinal));
        foreach (var path in paths)
        {
            var response = await client.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} respondió {(int)response.StatusCode}.");
        }
    }

    [Theory]
    [InlineData("/img/local133-logo-48.png")]
    [InlineData("/img/local133-logo-96.png")]
    [InlineData("/img/local133-logo-144.png")]
    [InlineData("/img/local133-logo-320.png")]
    [InlineData("/img/local133-logo-480.png")]
    [InlineData("/icon-512.png")]
    [InlineData("/icon-maskable-512.png")]
    [InlineData("/img/email/local133-email-logo.png")]
    public async Task Sirve_el_logo_y_los_iconos(string path)
    {
        var response = await CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<string> HtmlAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    // href="/…" o src="/…" locales (no "//cdn"), sin query ni fragmento.
    [GeneratedRegex("(?:href|src)=\"(/(?!/)[^\"?#]*)")]
    private static partial Regex LocalAssetRegex();
}
