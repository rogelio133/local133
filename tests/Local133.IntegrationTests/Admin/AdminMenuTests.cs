using System.Net;
using System.Text.RegularExpressions;
using Local133.IntegrationTests.Infrastructure;

namespace Local133.IntegrationTests.Admin;

/// <summary>
/// Menú del admin (ajustes 2026-11-26, punto 13): cada liga es una rejilla ícono | etiqueta, «Mi cuenta» va al final y en
/// escritorio el menú se oculta a un riel de íconos con admin-menu.js (estado en localStorage, aplicado desde el head).
/// </summary>
public partial class AdminMenuTests(Local133WebFactory factory) : AdminTestBase(factory)
{
    [Fact]
    public async Task Cada_liga_separa_el_icono_de_la_etiqueta_y_Mi_cuenta_va_al_final()
    {
        var client = await AdminAsync();

        var html = await GetHtmlAsync(client, "/admin/clientes");

        var labels = NavLabelRegex().Matches(html).Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(["Clientes", "Tablero", "Transacciones", "Webhooks", "Parámetros", "Mi cuenta"], labels);
        Assert.Equal(labels.Count, Regex.Matches(html, "admin-nav-icon").Count);
        Assert.Equal(labels.Count, Regex.Matches(html, "data-admin-nav-link").Count);
        // El ícono ya no va pegado al texto con me-2 (al partirse, la segunda línea quedaba debajo del ícono).
        Assert.DoesNotContain("admin-nav-icon me-2", html);
        Assert.Contains("class=\"nav-link admin-nav-link active\" href=\"/admin/clientes\"", html);
    }

    [Fact]
    public async Task El_menu_se_puede_ocultar_y_su_estado_se_aplica_antes_de_pintar()
    {
        var client = await AdminAsync();

        var html = await GetHtmlAsync(client, "/admin/clientes");
        var head = html[..html.IndexOf("</head>", StringComparison.Ordinal)];

        Assert.Contains("localStorage.getItem('l133-admin-menu') === 'collapsed'", head);
        Assert.Contains("classList.add('admin-menu-collapsed')", head);

        var toggle = ToggleRegex().Match(html);
        Assert.True(toggle.Success, "Falta el botón para ocultar el menú.");
        Assert.Contains("aria-controls=\"admin-menu\"", toggle.Value);
        Assert.Contains("aria-expanded=\"true\"", toggle.Value);
        Assert.Contains("aria-label=\"Ocultar menú\"", toggle.Value);
        // En el borde derecho del menú (columna admin-menu-handle, solo ≥lg), ya no en la barra superior.
        Assert.Contains("<div class=\"admin-menu-handle d-none d-lg-block\">", html);
        var navbar = html[..html.IndexOf("</nav>", StringComparison.Ordinal)];
        Assert.DoesNotContain("data-admin-menu-toggle", navbar);
        Assert.True(html.IndexOf("admin-menu-handle", StringComparison.Ordinal) > html.IndexOf("id=\"admin-menu\"", StringComparison.Ordinal));

        var script = ScriptRegex().Match(html);
        Assert.True(script.Success, "Falta admin-menu.js.");
        var response = await client.GetAsync(WebUtility.HtmlDecode(script.Groups[1].Value));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("l133-admin-menu", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task El_vendedor_no_carga_el_menu_del_admin()
    {
        var merchant = await ActiveMerchantAsync(Email);

        var html = await GetHtmlAsync(merchant, "/app");

        Assert.DoesNotContain("admin-menu.js", html);
        Assert.DoesNotContain("l133-admin-menu", html);
    }

    [GeneratedRegex("<span class=\"admin-nav-label\">([^<]+)</span>")]
    private static partial Regex NavLabelRegex();

    [GeneratedRegex("<button[^>]*data-admin-menu-toggle[^>]*>")]
    private static partial Regex ToggleRegex();

    // MapStaticAssets pone huella en la URL (/js/admin-menu.{huella}.js).
    [GeneratedRegex("<script src=\"(/js/admin-menu\\.[^\"]*js[^\"]*)\"></script>")]
    private static partial Regex ScriptRegex();
}
