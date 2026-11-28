using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Local133.Application.Auth;
using Local133.Application.Merchants;
using Local133.Application.Storage;
using Local133.Domain.Merchants;
using Local133.Domain.Products;
using Local133.Infrastructure.Identity;
using Local133.IntegrationTests.Infrastructure;
using Local133.IntegrationTests.Products;
using Local133.IntegrationTests.Sales;
using Local133.Web.Hosting;
using Local133.Web.PublicPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Local133.IntegrationTests.Merchants;

/// <summary>
/// 5.10: catálogo público y su pantalla «Catálogo en línea» (<c>/app/tienda</c>): publicar solo en Pro y con nombre del
/// negocio, slug único, «no disponible» si no se puede ver, imágenes públicas con la misma regla y vista previa. Desde el
/// 2026-11-27 la portada dibuja el catálogo (<c>/tienda/{slug}</c>) con <c>GET /api/public/tiendas/{slug}</c>; la liga
/// vieja del portal redirige ahí.
/// </summary>
public class StorefrontTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    private const string SettingsUrl = "/app/tienda";
    private const string PublishUrl = "/app/tienda/publicar";
    private const string UnpublishUrl = "/app/tienda/despublicar";

    [Fact]
    public async Task En_Pro_propone_la_liga_publica_y_el_catalogo_se_ve()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedTrialAsync(Email);
        await SetPhoneAsync(Email, "5512345678");
        await CreateProductAsync(Email, "Taco al pastor", 25m);
        await CreateProductAsync(Email, "Agua de horchata", 30m);

        var settings = await GetHtmlAsync(client, SettingsUrl);
        Assert.Contains("value=\"tacos-don-ramon\"", settings);
        Assert.Contains("data-storefront-form", settings);
        Assert.Contains("•••• 5678", settings);

        var publish = await PublishAsync(client, "tacos-don-ramon");
        Assert.Equal(HttpStatusCode.Redirect, publish.StatusCode);
        var merchant = await WithDbAsync(db => MerchantOfAsync(db, Email));
        Assert.True(merchant.PublicCatalogEnabled);
        Assert.Equal("tacos-don-ramon", merchant.PublicSlug);

        // La liga que se comparte es la de la portada.
        var after = WebUtility.HtmlDecode(await GetHtmlAsync(client, SettingsUrl));
        Assert.Contains(PublicCatalogMessages.Published, after);
        Assert.Contains("data-storefront-published", after);
        Assert.Contains($"value=\"{Site.StorefrontUrlFor("tacos-don-ramon")}\"", after);

        var response = await LandingClient().GetAsync(PublicPagesApi.Storefront("tacos-don-ramon"));
        var json = await response.Content.ReadAsStringAsync();
        var storefront = JsonSerializer.Deserialize<PublicStorefront>(json, JsonSerializerOptions.Web);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(storefront);
        Assert.True(storefront.Available);
        Assert.Equal("Tacos Don Ramón", storefront.BusinessName);
        Assert.Equal(["Agua de horchata", "Taco al pastor"], storefront.Products.Select(p => p.Name));
        Assert.Equal("$25.00", storefront.Products[1].PriceText);
        Assert.StartsWith("https://wa.me/525512345678?text=", storefront.WhatsAppUrl);
        Assert.Equal(merchant.ReferralCode, storefront.ReferralCode);
        Assert.Equal(2, storefront.TotalCount);
        // Nada interno.
        Assert.DoesNotContain(TestClabe, json);
    }

    [Fact]
    public async Task El_servidor_normaliza_la_liga_y_rechaza_una_invalida()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedTrialAsync(Email);

        var invalid = await PublishAsync(client, "ab");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains(PublicCatalogMessages.InvalidSlug, WebUtility.HtmlDecode(await invalid.Content.ReadAsStringAsync()));
        Assert.False((await WithDbAsync(db => MerchantOfAsync(db, Email))).PublicCatalogEnabled);

        var normalized = await PublishAsync(client, "  Tacos Don Ramón #2 ");
        Assert.Equal(HttpStatusCode.Redirect, normalized.StatusCode);
        Assert.Equal("tacos-don-ramon-2", (await WithDbAsync(db => MerchantOfAsync(db, Email))).PublicSlug);
    }

    [Fact]
    public async Task Una_liga_de_otro_vendedor_no_se_puede_usar()
    {
        await ActiveMerchantAsync(OtherEmail);
        await UpdateMerchantAsync(OtherEmail, m => m.PublishCatalog("tacos"));
        var client = await ActiveMerchantAsync(Email);
        await SeedTrialAsync(Email);

        var response = await PublishAsync(client, "tacos");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(PublicCatalogMessages.SlugTaken, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Null((await WithDbAsync(db => MerchantOfAsync(db, Email))).PublicSlug);
    }

    [Fact]
    public async Task Sin_nombre_del_negocio_no_se_publica()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedTrialAsync(Email);
        await UpdateMerchantAsync(Email, m => m.BusinessName = null);

        Assert.Contains("data-storefront-business-name", await GetHtmlAsync(client, SettingsUrl));
        var response = await PublishAsync(client, "mi-puesto");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(PublicCatalogMessages.BusinessNameRequired, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Null((await WithDbAsync(db => MerchantOfAsync(db, Email))).PublicSlug);
    }

    [Fact]
    public async Task Antes_del_primer_cobro_no_se_publica_y_dice_que_se_activa_con_la_prueba()
    {
        // Activo sin suscripción: su prueba empieza con su primer cobro (5.13).
        var client = await ActiveMerchantAsync(Email);

        var settings = WebUtility.HtmlDecode(await GetHtmlAsync(client, SettingsUrl));
        Assert.Contains("data-pro-required", settings);
        Assert.Contains("Se activa con tu prueba: tus 15 días de Pro empiezan con tu primer cobro.", settings);
        Assert.DoesNotContain("data-storefront-form", settings);

        var response = await PublishAsync(client, "tacos-don-ramon");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(PublicCatalogMessages.TrialRequired, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Null((await WithDbAsync(db => MerchantOfAsync(db, Email))).PublicSlug);
    }

    [Fact]
    public async Task En_Basico_no_se_publica_y_la_liga_dice_no_disponible()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedBasicPlanAsync(Email);
        await CreateProductAsync(Email, "Taco al pastor", 25m);

        var settings = WebUtility.HtmlDecode(await GetHtmlAsync(client, SettingsUrl));
        Assert.Contains("data-pro-required", settings);
        Assert.Contains("Tu catálogo de 1 producto te espera en Pro.", settings);
        Assert.DoesNotContain("data-storefront-form", settings);

        var response = await PublishAsync(client, "tacos-don-ramon");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(PublicCatalogMessages.PlanRequired, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Null((await WithDbAsync(db => MerchantOfAsync(db, Email))).PublicSlug);

        // Publicado cuando tenía Pro: la liga sigue, pero no muestra nada.
        await UpdateMerchantAsync(Email, m => m.PublishCatalog("tacos-don-ramon"));
        Assert.Contains("data-storefront-hidden-by-plan", await GetHtmlAsync(client, SettingsUrl));
        await AssertUnavailableAsync("tacos-don-ramon", "Taco al pastor");
    }

    [Fact]
    public async Task Al_dejar_de_publicar_la_liga_dice_no_disponible_y_conserva_el_slug()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedTrialAsync(Email);
        await PublishAsync(client, "tacos-don-ramon");

        var response = await PostFormAsync(client, SettingsUrl, new Dictionary<string, string>(), postUrl: UnpublishUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var merchant = await WithDbAsync(db => MerchantOfAsync(db, Email));
        Assert.False(merchant.PublicCatalogEnabled);
        Assert.Equal("tacos-don-ramon", merchant.PublicSlug);
        await AssertUnavailableAsync("tacos-don-ramon");
        // Al volver a publicar, propone la misma liga.
        Assert.Contains("value=\"tacos-don-ramon\"", await GetHtmlAsync(client, SettingsUrl));
    }

    [Theory]
    [InlineData(MerchantStatus.Suspended)]
    [InlineData(MerchantStatus.Disabled)]
    public async Task Suspendido_o_dado_de_baja_no_se_ve(MerchantStatus status)
    {
        await ActiveMerchantAsync(Email);
        await SeedTrialAsync(Email);
        await UpdateMerchantAsync(Email, m =>
        {
            m.PublishCatalog("tacos-don-ramon");
            m.Status = status;
        });

        await AssertUnavailableAsync("tacos-don-ramon");
    }

    [Theory]
    [InlineData("no-existe")]
    [InlineData("x")]
    public async Task Una_liga_que_no_existe_es_404(string slug)
    {
        var response = await LandingClient().GetAsync(PublicPagesApi.Storefront(slug));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task La_liga_no_distingue_mayusculas()
    {
        await ActiveMerchantAsync(Email);
        await SeedTrialAsync(Email);
        await UpdateMerchantAsync(Email, m => m.PublishCatalog("tacos-don-ramon"));

        var storefront = await LandingClient().GetFromJsonAsync<PublicStorefront>(PublicPagesApi.Storefront("Tacos-Don-Ramon"));

        Assert.True(storefront?.Available);
    }

    [Fact]
    public async Task Busca_y_pagina_los_productos()
    {
        await ActiveMerchantAsync(Email);
        await SeedTrialAsync(Email);
        await UpdateMerchantAsync(Email, m => m.PublishCatalog("tacos-don-ramon"));
        await CreateProductAsync(Email, "Taco al pastor", 25m);
        await CreateProductAsync(Email, "Agua de horchata", 30m);

        var found = await LandingClient().GetFromJsonAsync<PublicStorefront>(PublicPagesApi.Storefront("tacos-don-ramon", "taco"));
        Assert.NotNull(found);
        Assert.Equal("taco", found.Query);
        Assert.Equal("Taco al pastor", Assert.Single(found.Products).Name);

        // Una página de más se ajusta a la última.
        var last = await LandingClient().GetFromJsonAsync<PublicStorefront>(PublicPagesApi.Storefront("tacos-don-ramon", page: 9));
        Assert.NotNull(last);
        Assert.Equal(1, last.Page);
        Assert.Equal(1, last.TotalPages);
    }

    [Fact]
    public async Task La_liga_vieja_del_portal_redirige_a_la_portada_con_la_busqueda()
    {
        var response = await LandingClient().GetAsync("/tienda/tacos-don-ramon?q=taco&pagina=2");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal($"{Site.StorefrontUrlFor("tacos-don-ramon")}?q=taco&pagina=2", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Las_imagenes_publicas_solo_se_sirven_si_el_catalogo_se_ve()
    {
        await ActiveMerchantAsync(Email);
        await SeedTrialAsync(Email);
        var logoKey = await StoreMerchantImageAsync(Email, MerchantImageKind.Logo);
        var photoKey = await StoreMerchantImageAsync(Email, MerchantImageKind.ShopPhoto);
        var productId = await CreateProductAsync(Email, "Taco al pastor", 25m);
        var productKey = await StoreProductPhotoAsync(Email, productId);
        await UpdateMerchantAsync(Email, m => m.PublishCatalog("tacos-don-ramon"));

        var client = LandingClient();
        var json = await client.GetStringAsync(PublicPagesApi.Storefront("tacos-don-ramon"));
        var storefront = JsonSerializer.Deserialize<PublicStorefront>(json, JsonSerializerOptions.Web)!;
        var logoUrl = $"/tienda/tacos-don-ramon/logo?v={MerchantImageRules.Version(logoKey)}";
        var photoUrl = $"/tienda/tacos-don-ramon/foto?v={MerchantImageRules.Version(photoKey)}";
        var thumbUrl = $"/tienda/tacos-don-ramon/productos/{productId}/miniatura?v={productKey.Split('/').Last()}";
        // Absolutas: la portada las pide al portal.
        Assert.Equal(new Uri(client.BaseAddress!, logoUrl).AbsoluteUri, storefront.LogoUrl);
        Assert.Equal(new Uri(client.BaseAddress!, photoUrl).AbsoluteUri, storefront.CoverUrl);
        Assert.Equal(new Uri(client.BaseAddress!, thumbUrl).AbsoluteUri, Assert.Single(storefront.Products).ThumbnailUrl);
        // Nunca las rutas privadas del vendedor.
        Assert.DoesNotContain("/app/", json);

        foreach (var url in new[] { logoUrl, photoUrl, thumbUrl, $"/tienda/tacos-don-ramon/productos/{productId}/foto" })
        {
            var image = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, image.StatusCode);
            Assert.True(image.Headers.CacheControl?.Public);
        }

        // Producto de otro vendedor (por id) o dado de baja: 404.
        await ActiveMerchantAsync(OtherEmail);
        var otherProduct = await CreateProductAsync(OtherEmail, "Refresco", 20m);
        await StoreProductPhotoAsync(OtherEmail, otherProduct);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/tienda/tacos-don-ramon/productos/{otherProduct}/miniatura")).StatusCode);
        await WithDbAsync(async db =>
        {
            var product = await db.Products.IgnoreQueryFilters().SingleAsync(p => p.Id == productId);
            product.Delete(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(thumbUrl)).StatusCode);

        // Sin publicar: tampoco el logo.
        await UpdateMerchantAsync(Email, m => m.UnpublishCatalog());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(logoUrl)).StatusCode);
    }

    [Fact]
    public async Task Sin_celular_no_hay_boton_de_WhatsApp()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedTrialAsync(Email);
        await UpdateMerchantAsync(Email, m => m.PublishCatalog("tacos-don-ramon"));

        var storefront = await LandingClient().GetFromJsonAsync<PublicStorefront>(PublicPagesApi.Storefront("tacos-don-ramon"));

        Assert.NotNull(storefront);
        Assert.True(storefront.Available);
        Assert.Null(storefront.WhatsAppUrl);
        Assert.Contains("Agrega tu celular", await GetHtmlAsync(client, SettingsUrl));
    }

    [Fact]
    public async Task La_vista_previa_se_ve_sin_publicar_con_las_imagenes_privadas()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedExpiredPlanAsync(Email);
        var logoKey = await StoreMerchantImageAsync(Email, MerchantImageKind.Logo);
        await CreateProductAsync(Email, "Taco al pastor", 25m);

        var html = await GetHtmlAsync(client, "/app/tienda/vista-previa");

        Assert.Contains("data-storefront-preview", html);
        Assert.Contains("Taco al pastor", html);
        Assert.Contains($"src=\"/app/mi-informacion/logo?v={MerchantImageRules.Version(logoKey)}\"", html);
        Assert.Contains("<meta name=\"robots\" content=\"noindex, nofollow\" />", html);
    }

    [Fact]
    public async Task Cada_formulario_de_la_pagina_trae_su_token_antiforgery()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedTrialAsync(Email);
        await PublishAsync(client, "tacos-don-ramon");

        var html = await GetHtmlAsync(client, SettingsUrl);

        var forms = Regex.Matches(html, "<form[^>]*method=\"post\"[\\s\\S]*?</form>")
            .Select(m => m.Value)
            .Where(f => !f.Contains("logoutForm"))
            .ToList();
        Assert.Equal(2, forms.Count); // liga y dejar de publicar
        Assert.All(forms, form => Assert.Contains("name=\"__RequestVerificationToken\"", form));
    }

    [Fact]
    public async Task Admin_y_anonimo_no_entran_a_la_pantalla_del_vendedor()
    {
        await CreateConfirmedUserAsync("admin@prueba.mx", Roles.Admin);
        var admin = LandingClient();
        await LoginAsync(admin, "admin@prueba.mx");

        var asAdmin = await admin.GetAsync(SettingsUrl);
        Assert.Equal(HttpStatusCode.Redirect, asAdmin.StatusCode);
        Assert.StartsWith("/Identity/Account/AccessDenied", Location(asAdmin));

        var anonymous = await LandingClient().GetAsync("/app/tienda/vista-previa");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("/Identity/Account/Login", Location(anonymous));
    }

    [Fact]
    public async Task Mas_tiene_la_entrada_del_catalogo_en_linea()
    {
        var client = await ActiveMerchantAsync(Email);

        Assert.Contains("data-more-storefront", await GetHtmlAsync(client, "/app/mas"));
    }

    private static Task<HttpResponseMessage> PublishAsync(HttpClient client, string slug) =>
        PostFormAsync(client, SettingsUrl, new Dictionary<string, string> { ["Input.Slug"] = slug }, postUrl: PublishUrl);

    private PublicSiteOptions Site => Factory.Services.GetRequiredService<IOptions<PublicSiteOptions>>().Value;

    /// <summary>La liga existe pero no se ve: 200 con <c>available: false</c>, el nombre del negocio y nada más.</summary>
    private async Task AssertUnavailableAsync(string slug, string? mustNotContain = null)
    {
        var response = await LandingClient().GetAsync(PublicPagesApi.Storefront(slug));
        var json = await response.Content.ReadAsStringAsync();
        var storefront = JsonSerializer.Deserialize<PublicStorefront>(json, JsonSerializerOptions.Web);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(storefront);
        Assert.False(storefront.Available);
        Assert.Equal("Tacos Don Ramón", storefront.BusinessName);
        Assert.Empty(storefront.Products);
        Assert.Null(storefront.WhatsAppUrl);
        Assert.Null(storefront.ReferralCode);
        if (mustNotContain is not null)
        {
            Assert.DoesNotContain(mustNotContain, json);
        }
    }

    private async Task SetPhoneAsync(string email, string phone)
    {
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email) ?? throw new InvalidOperationException(email);
        Assert.True((await users.SetPhoneNumberAsync(user, phone)).Succeeded);
    }

    private async Task<string> StoreMerchantImageAsync(string email, MerchantImageKind kind)
    {
        var merchantId = await MerchantIdAsync(email);
        var key = MerchantImageRules.NewKey(merchantId, kind);
        var format = kind == MerchantImageKind.Logo ? SkiaSharp.SKEncodedImageFormat.Png : SkiaSharp.SKEncodedImageFormat.Jpeg;
        await SaveFileAsync(MerchantImageRules.FileKey(kind, key), TestImages.Solid(64, 64, format), MerchantImageRules.ContentType(kind));
        await UpdateMerchantAsync(email, m =>
        {
            if (kind == MerchantImageKind.Logo)
            {
                m.ReplaceLogo(key);
            }
            else
            {
                m.ReplaceShopPhoto(key);
            }
        });
        return key;
    }

    private async Task<string> StoreProductPhotoAsync(string email, int productId)
    {
        var merchantId = await MerchantIdAsync(email);
        var key = $"products/{merchantId}/{Guid.NewGuid():N}";
        var jpeg = TestImages.Solid(64, 64);
        await SaveFileAsync(ProductPhotoRulesFileKey(key), jpeg, "image/jpeg");
        await SaveFileAsync($"{key}-thumb.jpg", jpeg, "image/jpeg");
        await WithDbAsync(async db =>
        {
            var product = await db.Products.IgnoreQueryFilters().SingleAsync(p => p.Id == productId);
            product.PhotoKey = key;
            await db.SaveChangesAsync();
        });
        return key;
    }

    private static string ProductPhotoRulesFileKey(string key) => Application.Products.ProductPhotoRules.PhotoFileKey(key);

    private async Task SaveFileAsync(string key, byte[] content, string contentType)
    {
        using var scope = Factory.Services.CreateScope();
        await using var stream = new MemoryStream(content);
        await scope.ServiceProvider.GetRequiredService<IFileStorage>().SaveAsync(key, stream, contentType);
    }
}
