using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Local133.LandingPage.Services;
using Local133.Web.PublicPages;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Local133.LandingPage.Tests;

/// <summary>
/// Recibo (<c>/recibo/{token}</c>) y catálogo (<c>/tienda/{slug}</c>) públicos, en la portada desde el 2026-11-27. Los
/// datos salen de <c>/api/public</c> del portal; aquí ese API es un doble (<see cref="StubPortal"/>), así que corren en CI.
/// Las reglas (plan, marca, visibilidad) se prueban en el portal (IntegrationTests).
/// </summary>
public sealed class PublicPagesTests : IDisposable
{
    private const string AppUrl = "https://app.example.test";
    private const string Token = "0123456789abcdef0123456789abcdef";
    private const string ApiKey = "llave-de-prueba-de-la-portada";

    private readonly StubPortal portal = new(ApiKey);
    private readonly WebApplicationFactory<Program> factory;

    public PublicPagesTests() => factory = CreateFactory();

    /// <param name="settings">Además de las de siempre (gana la última).</param>
    private WebApplicationFactory<Program> CreateFactory(params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Sass:Watch", "false");
            builder.UseSetting("Landing:AppUrl", AppUrl);
            builder.UseSetting(PublicPagesApi.KeyConfigKey, ApiKey);
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient<PublicPagesClient>().ConfigurePrimaryHttpMessageHandler(() => portal);
                // Reintentos sin espera: la falla del portal no tarda segundos.
                services.PostConfigureAll<HttpStandardResilienceOptions>(options =>
                {
                    options.Retry.Delay = TimeSpan.Zero;
                    options.Retry.UseJitter = false;
                    options.Retry.BackoffType = DelayBackoffType.Constant;
                });
            });
        });

    public void Dispose() => factory.Dispose();

    private HttpClient Client(WebApplicationFactory<Program>? site = null) =>
        (site ?? factory).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string TextOf(string html) =>
        Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]*?>", " ")), @"\s+", " ");

    private static PublicReceipt Receipt(string? logoUrl = null, string? referralCode = null, bool cart = false) => new(
        "Tacos Don Ramón",
        "$70.00",
        "Comida",
        "12 de noviembre, 3:45 p. m.",
        cart ? [new PublicReceiptItem("Taco al pastor", 2, "$25.00", "$50.00"), new PublicReceiptItem("Refresco", 1, "$20.00", "$20.00")] : [],
        [new PublicReceiptPayment("12 de noviembre, 3:45 p. m.", "$70.00", "0192191042")],
        logoUrl,
        referralCode);

    private static PublicStorefront Storefront(int totalCount = 2, int page = 1, string? query = null) => new(
        true,
        "Tacos Don Ramón",
        "Calle 5 de Mayo 12\nCentro",
        $"{AppUrl}/tienda/tacos-don-ramon/logo?v=1",
        $"{AppUrl}/tienda/tacos-don-ramon/foto?v=1",
        "https://wa.me/525512345678?text=Hola",
        [
            new PublicStorefrontProduct("Agua de horchata", null, "$30.00", null),
            new PublicStorefrontProduct("Taco al pastor", "Con piña", "$25.00", $"{AppUrl}/tienda/tacos-don-ramon/productos/7/miniatura?v=1"),
        ],
        page,
        PublicStorefront.DefaultPageSize,
        totalCount,
        query,
        "ABC234");

    // --- Recibo -----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task El_recibo_se_ve_sin_encabezado_ni_pie_y_dice_que_no_es_oficial()
    {
        portal.Respond(PublicPagesApi.Receipt(Token), Receipt());

        var response = await Client().GetAsync($"/recibo/{Token}");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var text = TextOf(html);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"{AppUrl}{PublicPagesApi.Receipt(Token)}", Assert.Single(portal.Requests));
        Assert.Contains("data-receipt-business>Tacos Don Ramón<", html);
        Assert.Contains("data-receipt-amount>$70.00<", html);
        Assert.Contains("data-receipt-tracking>0192191042<", html);
        Assert.Contains("Este recibo no es un comprobante fiscal ni oficial", text);
        Assert.DoesNotContain("<header", html);
        Assert.DoesNotContain("<footer", html);
        Assert.DoesNotContain("site-header", html);
        Assert.DoesNotContain("site-footer", html);
        Assert.DoesNotContain("data-receipt-folio", html);
        Assert.DoesNotContain("manifest", html);
        Assert.Contains("class=\"receipt-page\"", html);
        Assert.Contains("/lib/bootstrap-icons/font/bootstrap-icons.min.css", html);
        Assert.Contains("/js/receipt-print.js", html);
        Assert.Contains("<meta name=\"robots\" content=\"noindex, nofollow\" />", html);
        Assert.Equal("noindex, nofollow", string.Join(",", response.Headers.GetValues("X-Robots-Tag")));
        Assert.Equal("no-referrer", string.Join(",", response.Headers.GetValues("Referrer-Policy")));
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Sin_marca_dice_Cobrado_con_Local133_con_el_codigo_de_invitacion()
    {
        portal.Respond(PublicPagesApi.Receipt(Token), Receipt(referralCode: "ABC234"));

        var html = WebUtility.HtmlDecode(await Client().GetStringAsync($"/recibo/{Token}"));

        Assert.Contains("data-receipt-powered-by", html);
        Assert.Contains("href=\"/?codigo=ABC234\" target=\"_blank\" rel=\"noopener\"", html);
        Assert.Contains("Cobrado con <strong>Local133</strong>", html);
        Assert.DoesNotContain("data-receipt-logo", html);
    }

    [Fact]
    public async Task Con_marca_lleva_el_logo_del_portal_y_no_el_pie()
    {
        var logo = $"{AppUrl}/recibo/{Token}/logo?v=abc";
        portal.Respond(PublicPagesApi.Receipt(Token), Receipt(logoUrl: logo, cart: true));

        var html = WebUtility.HtmlDecode(await Client().GetStringAsync($"/recibo/{Token}"));

        Assert.Contains($"src=\"{logo}\"", html);
        Assert.DoesNotContain("data-receipt-powered-by", html);
        Assert.Equal(2, html.Split("data-receipt-item>").Length - 1);
        Assert.Contains("2 × $25.00", html);
        Assert.Contains("data-receipt-total>$70.00<", html);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    public async Task Un_token_con_otra_forma_es_404_sin_preguntar_al_portal(string token)
    {
        var response = await Client().GetAsync($"/recibo/{token}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("No encontramos este recibo", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Empty(portal.Requests);
    }

    [Fact]
    public async Task Un_recibo_que_el_portal_no_tiene_es_404()
    {
        var response = await Client().GetAsync($"/recibo/{Token}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("No encontramos este recibo", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Equal("noindex, nofollow", string.Join(",", response.Headers.GetValues("X-Robots-Tag")));
    }

    [Fact]
    public async Task Si_el_portal_falla_el_recibo_responde_503()
    {
        portal.Fail(PublicPagesApi.Receipt(Token));

        var response = await Client().GetAsync($"/recibo/{Token}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("No pudimos cargar el recibo", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    // --- Catálogo ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task El_catalogo_lleva_la_barra_rosa_con_el_logo_a_la_portada_y_no_tiene_pie()
    {
        portal.Respond(PublicPagesApi.Storefront("tacos-don-ramon"), Storefront());

        var response = await Client().GetAsync("/tienda/tacos-don-ramon");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches("<header class=\"store-bar[^\"]*\">\\s*<a class=\"store-bar-brand\" href=\"/\"", html);
        Assert.Contains("src=\"/img/local133-logo-48.png\"", html);
        Assert.DoesNotContain("<footer", html);
        Assert.DoesNotContain("site-header", html);
        Assert.Contains("data-storefront-name>Tacos Don Ramón<", html);
        Assert.Matches(@"<br />\s*Centro", html);
        Assert.Contains($"src=\"{AppUrl}/tienda/tacos-don-ramon/foto?v=1\"", html);
        Assert.Contains($"src=\"{AppUrl}/tienda/tacos-don-ramon/productos/7/miniatura?v=1\"", html);
        Assert.Contains("href=\"https://wa.me/525512345678?text=Hola\"", html);
        Assert.Contains("href=\"/?codigo=ABC234\"", html);
        Assert.Contains("action=\"/tienda/tacos-don-ramon\"", html);
        Assert.DoesNotContain("data-pager", html);
        Assert.DoesNotContain("noindex", html);
        Assert.False(response.Headers.Contains("X-Robots-Tag"));
        Assert.DoesNotContain("data-storefront-category", html); // sin agrupar: una sola lista
        Assert.Single(Regex.Matches(html, "data-storefront-products"));
    }

    [Fact]
    public async Task El_catalogo_agrupado_pone_un_encabezado_por_categoria_y_Otros_al_final()
    {
        // Ajustes 2026-11-26: el portal manda los productos ya en el orden de sus categorías.
        var grouped = Storefront(totalCount: 4) with
        {
            Grouped = true,
            Products =
            [
                new PublicStorefrontProduct("Agua de horchata", null, "$30.00", null, "Bebidas"),
                new PublicStorefrontProduct("Gringa", null, "$45.00", null, "Tacos"),
                new PublicStorefrontProduct("Taco al pastor", null, "$25.00", null, "Tacos"),
                new PublicStorefrontProduct("Servilletas", null, "$5.00", null),
            ],
        };
        portal.Respond(PublicPagesApi.Storefront("tacos-don-ramon"), grouped);

        var html = WebUtility.HtmlDecode(await Client().GetStringAsync("/tienda/tacos-don-ramon"));

        Assert.Equal(["Bebidas", "Tacos", "Otros"],
            Regex.Matches(html, "data-storefront-category>([^<]+)</h2>").Select(m => m.Groups[1].Value));
        Assert.Equal(3, Regex.Matches(html, "data-storefront-products").Count);
        Assert.Equal(4, Regex.Matches(html, "data-storefront-product>").Count);
    }

    [Fact]
    public async Task Pasa_la_busqueda_y_la_pagina_al_portal_y_pagina_con_la_misma_busqueda()
    {
        portal.Respond(PublicPagesApi.Storefront("tacos-don-ramon", "taco", 2), Storefront(totalCount: 60, page: 2, query: "taco"));

        var html = WebUtility.HtmlDecode(await Client().GetStringAsync("/tienda/Tacos-Don-Ramon?q=%20taco%20&pagina=2"));

        Assert.Equal($"{AppUrl}/api/public/tiendas/tacos-don-ramon?q=taco&pagina=2", Assert.Single(portal.Requests));
        Assert.Contains("Página 2 de 3", html);
        Assert.Contains("href=\"/tienda/tacos-don-ramon?q=taco\" rel=\"prev\"", html);
        Assert.Contains("href=\"/tienda/tacos-don-ramon?q=taco&pagina=3\" rel=\"next\"", html);
        Assert.Contains("para «taco»", html);
    }

    [Fact]
    public async Task Un_catalogo_que_no_se_ve_dice_no_disponible_con_noindex()
    {
        portal.Respond(PublicPagesApi.Storefront("tacos-don-ramon"), PublicStorefront.Unavailable("Tacos Don Ramón"));

        var response = await Client().GetAsync("/tienda/tacos-don-ramon");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-storefront-unavailable", html);
        Assert.Contains("Catálogo no disponible por ahora", html);
        Assert.Contains("El catálogo de Tacos Don Ramón no está disponible", html);
        Assert.Contains("class=\"store-bar", html);
        Assert.Contains("<meta name=\"robots\" content=\"noindex, nofollow\" />", html);
        Assert.Equal("noindex, nofollow", string.Join(",", response.Headers.GetValues("X-Robots-Tag")));
    }

    [Fact]
    public async Task Una_liga_que_no_existe_es_404_con_la_misma_pagina()
    {
        var response = await Client().GetAsync("/tienda/no-existe");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("data-storefront-unavailable", html);
        Assert.Contains("Este catálogo no está disponible en este momento", html);
    }

    [Fact]
    public async Task Si_el_portal_falla_el_catalogo_responde_503()
    {
        portal.Fail(PublicPagesApi.Storefront("tacos-don-ramon"));

        var response = await Client().GetAsync("/tienda/tacos-don-ramon");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("No pudimos cargar el catálogo", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    // --- Llave y límite de peticiones (2026-11-27) ---------------------------------------------------------------------------

    [Fact]
    public async Task Manda_la_llave_al_portal()
    {
        portal.Respond(PublicPagesApi.Receipt(Token), Receipt());

        await Client().GetStringAsync($"/recibo/{Token}");

        Assert.Equal(ApiKey, Assert.Single(portal.Keys));
    }

    [Fact]
    public async Task Sin_la_llave_el_portal_la_rechaza_y_las_paginas_dicen_que_no_cargaron()
    {
        portal.Respond(PublicPagesApi.Receipt(Token), Receipt());
        portal.Respond(PublicPagesApi.Storefront("tacos-don-ramon"), Storefront());
        using var site = CreateFactory((PublicPagesApi.KeyConfigKey, ""));

        var receipt = await Client(site).GetAsync($"/recibo/{Token}");
        var storefront = await Client(site).GetAsync("/tienda/tacos-don-ramon");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, receipt.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, storefront.StatusCode);
        Assert.Contains("No pudimos cargar el recibo", WebUtility.HtmlDecode(await receipt.Content.ReadAsStringAsync()));
        Assert.DoesNotContain("Tacos Don Ramón", WebUtility.HtmlDecode(await receipt.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Al_pasarse_del_limite_por_IP_responde_429_sin_llamar_al_portal()
    {
        portal.Respond(PublicPagesApi.Receipt(Token), Receipt());
        using var site = CreateFactory(("RateLimiting:ReceiptPermitLimit", "2"), ("RateLimiting:WindowSeconds", "60"));
        var client = Client(site);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/recibo/{Token}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/recibo/no-es-token")).StatusCode); // también cuenta
        var rejected = await client.GetAsync($"/recibo/{Token}");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter);
        Assert.Contains("Demasiadas solicitudes", await rejected.Content.ReadAsStringAsync());
        Assert.Single(portal.Requests);
    }

    [Fact]
    public async Task El_limite_del_recibo_no_gasta_el_del_catalogo_ni_el_de_la_portada()
    {
        portal.Respond(PublicPagesApi.Receipt(Token), Receipt());
        portal.Respond(PublicPagesApi.Storefront("tacos-don-ramon"), Storefront());
        using var site = CreateFactory(("RateLimiting:ReceiptPermitLimit", "1"), ("RateLimiting:StorefrontPermitLimit", "1"));
        var client = Client(site);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/recibo/{Token}")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync($"/recibo/{Token}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/tienda/tacos-don-ramon")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/tienda/tacos-don-ramon")).StatusCode);
        // La portada, las legales y los archivos no tienen límite.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/terminos")).StatusCode);
    }

    [Fact]
    public async Task El_limite_es_por_la_IP_que_reporta_el_front_end_de_App_Service()
    {
        portal.Respond(PublicPagesApi.Receipt(Token), Receipt());
        using var site = CreateFactory(("RateLimiting:ReceiptPermitLimit", "1"));
        var client = Client(site);

        Task<HttpResponseMessage> FromAsync(string forwardedFor)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"/recibo/{Token}");
            request.Headers.Add("X-Forwarded-For", forwardedFor);
            return client.SendAsync(request);
        }

        Assert.Equal(HttpStatusCode.OK, (await FromAsync("203.0.113.10")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await FromAsync("203.0.113.10")).StatusCode);
        // Otro cliente tiene su propio límite.
        Assert.Equal(HttpStatusCode.OK, (await FromAsync("203.0.113.20")).StatusCode);
        // Solo cuenta el último valor (el que agrega el front-end): inventar uno antes no da un límite nuevo.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await FromAsync("198.51.100.99, 203.0.113.20")).StatusCode);
    }

    [Fact]
    public void Las_rutas_de_la_API_escapan_y_omiten_lo_vacio()
    {
        Assert.Equal("/api/public/recibos/abc", PublicPagesApi.Receipt("abc"));
        Assert.Equal("/api/public/tiendas/tacos", PublicPagesApi.Storefront("tacos", " ", 1));
        Assert.Equal("/api/public/tiendas/tacos?q=agua%20fresca&pagina=3", PublicPagesApi.Storefront("tacos", "agua fresca", 3));
        Assert.Equal("/api/public/tiendas/a%2Fb", PublicPagesApi.Storefront("a/b"));
    }

    /// <summary>
    /// Doble del portal: como el real, sin la llave responde 401; con ella, JSON por ruta (o falla con 500), y lo demás
    /// es 404.
    /// </summary>
    private sealed class StubPortal(string apiKey) : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string, string> responses = new();
        private readonly ConcurrentDictionary<string, bool> failures = new();

        public ConcurrentQueue<string> Requests { get; } = new();

        /// <summary>Llave que trajo cada petición (vacía si no traía).</summary>
        public ConcurrentQueue<string> Keys { get; } = new();

        public void Respond<T>(string pathAndQuery, T body) =>
            responses[pathAndQuery] = JsonSerializer.Serialize(body, JsonSerializerOptions.Web);

        public void Fail(string pathAndQuery) => failures[pathAndQuery] = true;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var pathAndQuery = uri.PathAndQuery;
            var key = request.Headers.TryGetValues(PublicPagesApi.KeyHeader, out var values) ? string.Join(",", values) : "";
            Keys.Enqueue(key);
            if (key != apiKey)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }

            // Cada intento cuenta; con una falla se ven los reintentos, así que se registra solo el primero.
            if (!failures.ContainsKey(pathAndQuery) || Requests.IsEmpty)
            {
                Requests.Enqueue(uri.AbsoluteUri);
            }

            if (failures.ContainsKey(pathAndQuery))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            return Task.FromResult(responses.TryGetValue(pathAndQuery, out var json)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
