using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Local133.Application.Auth;
using Local133.IntegrationTests.Infrastructure;
using Local133.IntegrationTests.Sales;

namespace Local133.IntegrationTests.Home;

/// <summary>
/// PWA instalable (ajustes 2026-11-26, punto 12): manifest, service worker sin datos y página sin conexión. El manifest y
/// pwa.js van solo en las pantallas de la app (vendedor, admin, home e Identity), nunca en el recibo ni en la tienda.
/// </summary>
public partial class PwaTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    private const string AdminEmail = "admin@prueba.mx";

    [Fact]
    public async Task El_manifest_se_sirve_con_su_tipo_y_sus_iconos_existen()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/manifest.webmanifest");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/manifest+json", response.Content.Headers.ContentType?.MediaType);
        using var manifest = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = manifest.RootElement;
        Assert.Equal("Local133", root.GetProperty("name").GetString());
        Assert.Equal("Local133", root.GetProperty("short_name").GetString());
        Assert.Equal("es-MX", root.GetProperty("lang").GetString());
        Assert.Equal("/?source=pwa", root.GetProperty("start_url").GetString());
        Assert.Equal("/", root.GetProperty("scope").GetString());
        Assert.Equal("standalone", root.GetProperty("display").GetString());

        var icons = root.GetProperty("icons").EnumerateArray().ToList();
        Assert.Contains(icons, i => i.GetProperty("sizes").GetString() == "192x192");
        Assert.Contains(icons, i => i.GetProperty("sizes").GetString() == "512x512" && i.GetProperty("purpose").GetString() == "any");
        Assert.Contains(icons, i => i.GetProperty("purpose").GetString() == "maskable");
        foreach (var icon in icons)
        {
            var src = icon.GetProperty("src").GetString()!;
            var image = await client.GetAsync(src);
            Assert.True(image.StatusCode == HttpStatusCode.OK, $"{src} respondió {(int)image.StatusCode}.");
            Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task El_service_worker_se_sirve_sin_cache_y_no_guarda_las_pantallas()
    {
        var response = await CreateClient().GetAsync("/sw.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoCache, "sw.js debe ir con Cache-Control: no-cache.");
        var script = await response.Content.ReadAsStringAsync();
        Assert.Contains("/offline.html", script);
        // Solo escribe en la caché en el install (cache.addAll); nunca un cache.put de lo que responde el sitio.
        Assert.DoesNotContain(".put(", script);
    }

    [Fact]
    public async Task La_pagina_sin_conexion_existe()
    {
        var response = await CreateClient().GetAsync("/offline.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Sin conexión", html);
        Assert.Contains("location.reload()", html);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Identity/Account/Login")]
    public async Task Las_paginas_publicas_de_la_app_traen_el_manifest(string page)
    {
        var client = CreateClient();

        await AssertHasPwaAsync(client, await GetHtmlAsync(client, page));
    }

    [Fact]
    public async Task El_vendedor_trae_el_manifest_en_sus_pantallas_y_en_mi_cuenta()
    {
        var client = await ActiveMerchantAsync(Email);

        await AssertHasPwaAsync(client, await GetHtmlAsync(client, "/app"));
        await AssertHasPwaAsync(client, await GetHtmlAsync(client, "/app/mas"));
        await AssertHasPwaAsync(client, await GetHtmlAsync(client, "/Identity/Account/Manage/ChangePassword"));
    }

    [Fact]
    public async Task El_admin_trae_el_manifest()
    {
        await CreateConfirmedUserAsync(AdminEmail, Roles.Admin);
        var client = CreateClient();
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(client, AdminEmail)).StatusCode);

        await AssertHasPwaAsync(client, await GetHtmlAsync(client, "/admin/clientes"));
    }

    private static async Task AssertHasPwaAsync(HttpClient client, string html)
    {
        var head = html[..html.IndexOf("</head>", StringComparison.Ordinal)];
        var manifest = ManifestLinkRegex().Match(head);
        Assert.True(manifest.Success, "El <head> no trae el manifest.");
        Assert.Contains("<meta name=\"apple-mobile-web-app-capable\" content=\"yes\" />", head);
        Assert.Contains("<meta name=\"apple-mobile-web-app-title\" content=\"Local133\" />", head);
        Assert.Matches("<script src=\"/js/pwa\\.[^\"]*js\"></script>", html);

        // La liga lleva la huella de MapStaticAssets y responde.
        var response = await client.GetAsync(WebUtility.HtmlDecode(manifest.Groups[1].Value));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/manifest+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static void AssertHasNoPwa(string html)
    {
        Assert.DoesNotContain("rel=\"manifest\"", html);
        Assert.DoesNotContain("apple-mobile-web-app-capable", html);
        Assert.DoesNotContain("/js/pwa", html);
    }

    [GeneratedRegex("<link rel=\"manifest\" href=\"(/manifest\\.[^\"]*webmanifest)\" />")]
    private static partial Regex ManifestLinkRegex();
}
