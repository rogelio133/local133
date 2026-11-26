using System.Net;
using Local133.Application.Auth;
using Local133.Application.Merchants;
using Local133.Domain.Merchants;
using Local133.IntegrationTests.Infrastructure;
using Local133.IntegrationTests.Products;
using Local133.IntegrationTests.Sales;
using Microsoft.EntityFrameworkCore;

namespace Local133.IntegrationTests.Merchants;

/// <summary>
/// «Mi información» (/app/mi-informacion, 4.1): datos generales (con el celular de la cuenta), correo en solo
/// lectura, logo y foto del local, dado de baja en solo lectura, acceso por rol y aislamiento entre vendedores.
/// </summary>
public class ProfileTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    private const string Url = "/app/mi-informacion";
    private const string LogoUrl = "/app/mi-informacion/logo";
    private const string PhotoUrl = "/app/mi-informacion/foto";

    // --- Datos generales ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Muestra_los_datos_actuales_y_el_correo_en_solo_lectura()
    {
        var client = await MerchantClientAsync(Email);
        await UpdateMerchantAsync(Email, m => m.BusinessName = "Tacos Don Ramón");

        var html = await GetHtmlAsync(client, Url);

        Assert.Contains("value=\"María López\"", html);
        Assert.Contains("value=\"Tacos Don Ramón\"", WebUtility.HtmlDecode(html));
        Assert.Contains($"data-profile-email>{Email}</span>", html);
        Assert.Contains("href=\"/Identity/Account/Manage/Email\"", html);
        Assert.DoesNotContain("name=\"Input.Email\"", html);
        Assert.Contains("data-profile-image=\"logo\"", html);
        Assert.Contains("data-profile-image=\"shop-photo\"", html);
    }

    [Fact]
    public async Task Guarda_los_datos_normalizados_y_el_celular_de_la_cuenta()
    {
        var client = await MerchantClientAsync(Email);

        var response = await SaveAsync(client, "  María José  Núñez ", "  Tacos   Don Ramón ", "+52 55 1234 5678",
            "  Tianguis de los martes \r\n\r\n pasillo 4 ", "  Consumo  ");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Url, Location(response));
        var merchant = await WithDbAsync(db => MerchantOfAsync(db, Email));
        Assert.Equal("María José Núñez", merchant.OwnerName);
        Assert.Equal("Tacos Don Ramón", merchant.BusinessName);
        Assert.Equal("Tianguis de los martes\npasillo 4", merchant.Address);
        Assert.Equal("Consumo", merchant.DefaultConcept);
        var user = await WithDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Email == Email));
        Assert.Equal("5512345678", user.PhoneNumber);
        Assert.False(user.PhoneNumberConfirmed);

        // La sesión sigue viva (se refrescó la cookie tras el cambio de security stamp) y muestra el aviso.
        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, Url));
        Assert.Contains(ProfileMessages.Saved, html);
        Assert.Contains("value=\"5512345678\"", html);
        Assert.Contains("value=\"Consumo\"", html);
        Assert.Contains("Hola, María", await GetHtmlAsync(client, "/app"));
    }

    [Fact]
    public async Task El_concepto_por_defecto_vacio_se_borra()
    {
        var client = await MerchantClientAsync(Email);
        await UpdateMerchantAsync(Email, m => m.DefaultConcept = "Consumo");

        var response = await SaveAsync(client, "María López", "Tacos", "", "", "   ");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null((await WithDbAsync(db => MerchantOfAsync(db, Email))).DefaultConcept);
    }

    [Fact]
    public async Task Un_nombre_guardado_con_signos_se_propone_solo_con_letras()
    {
        // Ajustes 2026-11-26: los nombres anteriores con «.», «-» o «'» no se migran; se proponen limpios para que
        // guardar no falle.
        var client = await MerchantClientAsync(Email);
        await UpdateMerchantAsync(Email, m => m.OwnerName = "Ma. José O'Brien");

        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, Url));

        Assert.Contains("value=\"Ma José O Brien\"", html);
        Assert.Contains("Hola, Ma.", WebUtility.HtmlDecode(await GetHtmlAsync(client, "/app"))); // el saludo sigue igual
    }

    [Fact]
    public async Task Telefono_y_direccion_vacios_se_borran()
    {
        var client = await MerchantClientAsync(Email);
        Assert.Equal(HttpStatusCode.Redirect, (await SaveAsync(client, "María López", "Tacos", "5512345678", "Pasillo 4")).StatusCode);

        var response = await SaveAsync(client, "María López", "Tacos", "", "  ");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null((await WithDbAsync(db => MerchantOfAsync(db, Email))).Address);
        Assert.Null(await WithDbAsync(db => db.Users.Where(u => u.Email == Email).Select(u => u.PhoneNumber).SingleAsync()));
    }

    [Theory]
    [InlineData("", "Tacos", "", "", ProfileMessages.OwnerNameRequired)]
    [InlineData("María 2", "Tacos", "", "", ProfileMessages.OwnerNameInvalid)]
    [InlineData("O'Brien", "Tacos", "", "", ProfileMessages.OwnerNameInvalid)]
    [InlineData("Ma. Luisa", "Tacos", "", "", ProfileMessages.OwnerNameInvalid)]
    [InlineData("Ana-Luisa", "Tacos", "", "", ProfileMessages.OwnerNameInvalid)]
    [InlineData("María", "   ", "", "", ProfileMessages.BusinessNameRequired)]
    [InlineData("María", "Tacos · Don Ramón", "", "", ProfileMessages.BusinessNameInvalid)]
    [InlineData("María", "<b>Tacos</b>", "", "", ProfileMessages.BusinessNameInvalid)]
    [InlineData("María", "Tacos", "551234567", "", ProfileMessages.PhoneInvalid)]
    [InlineData("María", "Tacos", "", "Uno\nDos\nTres\nCuatro", ProfileMessages.AddressTooManyLines)]
    [InlineData("María", "Tacos", "", "Pasillo\u00074", ProfileMessages.AddressInvalid)]
    public async Task Datos_invalidos_no_guardan_nada(string owner, string business, string phone, string address, string message)
    {
        var client = await MerchantClientAsync(Email);
        await UpdateMerchantAsync(Email, m => m.BusinessName = "Negocio");

        var response = await SaveAsync(client, owner, business, phone, address);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(message, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        var merchant = await WithDbAsync(db => MerchantOfAsync(db, Email));
        Assert.Equal(OwnerName, merchant.OwnerName);
        Assert.Equal("Negocio", merchant.BusinessName);
        Assert.Null(merchant.Address);
    }

    [Theory]
    [InlineData("Tacos · Consumo", ProfileMessages.DefaultConceptInvalid)]
    [InlineData("Taco 🌮", ProfileMessages.DefaultConceptInvalid)]
    [InlineData(null, ProfileMessages.DefaultConceptTooLong)] // 101 caracteres
    public async Task Concepto_por_defecto_invalido_no_guarda_nada(string? concept, string message)
    {
        var client = await MerchantClientAsync(Email);

        var response = await SaveAsync(client, "María López", "Tacos", "", "", concept ?? new string('a', DefaultConcept.MaxLength + 1));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains(message, html);
        Assert.Contains("data-valmsg-for=\"Input.DefaultConcept\"", html);
        Assert.Null((await WithDbAsync(db => MerchantOfAsync(db, Email))).DefaultConcept);
    }

    [Fact]
    public async Task Nombre_del_negocio_de_121_caracteres_se_rechaza()
    {
        var client = await MerchantClientAsync(Email);

        var response = await SaveAsync(client, "María", new string('a', BusinessName.MaxLength + 1), "", "");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(ProfileMessages.BusinessNameTooLong, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task El_formulario_trae_la_validacion_en_cliente()
    {
        var client = await MerchantClientAsync(Email);

        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, Url));

        Assert.Contains($"data-val-required=\"{ProfileMessages.BusinessNameRequired}\"", html);
        Assert.Contains($"data-val-regex-pattern=\"{BusinessName.Pattern}\"", html);
        Assert.Contains($"data-val-regex-pattern=\"{PersonName.Pattern}\"", html);
        Assert.Contains($"data-val-phone=\"{ProfileMessages.PhoneInvalid}\"", html);
        Assert.Contains($"data-val-maxlines=\"{ProfileMessages.AddressTooManyLines}\"", html);
        Assert.Contains("data-val-maxlines-max=\"3\"", html);
        Assert.Contains("maxlength=\"200\"", html);
        Assert.Contains("/js/image-upload.", html);
        Assert.Contains("name=\"Input.DefaultConcept\"", html);
        Assert.Contains($"data-val-regex-pattern=\"{DefaultConcept.Pattern}\"", html);
        Assert.Contains($"data-val-length=\"{ProfileMessages.DefaultConceptTooLong}\"", html);
        Assert.Contains($"maxlength=\"{DefaultConcept.MaxLength}\"", html);
        Assert.Contains("data-letters-only", html);
        Assert.Contains("/js/letters-input.", html);
        Assert.Contains("Aparece en tus recibos y en tu catálogo en línea.", html);
    }

    [Fact]
    public async Task Cada_formulario_de_la_pagina_trae_su_token_antiforgery()
    {
        // Los helpers de las pruebas toman el token de cualquier formulario; el navegador solo manda el del suyo.
        var client = await MerchantClientAsync(Email);
        await UploadAsync(client, LogoUrl, TestImages.Solid(100, 100, SkiaSharp.SKEncodedImageFormat.Png), "logo.png");

        var html = await GetHtmlAsync(client, Url);

        var forms = System.Text.RegularExpressions.Regex.Matches(html, "<form[^>]*method=\"post\"[\\s\\S]*?</form>")
            .Select(m => m.Value)
            .Where(f => !f.Contains("logoutForm"))
            .ToList();
        Assert.Equal(4, forms.Count); // datos, logo, foto del local y quitar logo
        Assert.All(forms, form => Assert.Contains("name=\"__RequestVerificationToken\"", form));
    }

    [Fact]
    public async Task Sin_token_antiforgery_responde_400()
    {
        var client = await MerchantClientAsync(Email);

        var response = await client.PostAsync(Url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.OwnerName"] = "Ana",
            ["Input.BusinessName"] = "Tacos",
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(OwnerName, (await WithDbAsync(db => MerchantOfAsync(db, Email))).OwnerName);
    }

    [Fact]
    public async Task Dado_de_baja_es_de_solo_lectura()
    {
        var client = await MerchantClientAsync(Email);
        await UpdateMerchantAsync(Email, m => m.Status = MerchantStatus.Disabled);

        var html = await GetHtmlAsync(client, Url);
        Assert.Contains("data-profile-read-only", html);
        Assert.DoesNotContain("data-profile-submit", html);
        Assert.DoesNotContain("data-image-submit", html);

        var save = await SaveAsync(client, "Ana", "Tacos", "", "");
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        var logo = await UploadAsync(client, LogoUrl, MerchantImageProcessorTests.HalfTransparentPng(100, 100), "logo.png");
        Assert.Equal(HttpStatusCode.Redirect, logo.StatusCode);
        Assert.Contains(ProfileMessages.ReadOnly, WebUtility.HtmlDecode(await GetHtmlAsync(client, Url)));

        var merchant = await WithDbAsync(db => MerchantOfAsync(db, Email));
        Assert.Equal(OwnerName, merchant.OwnerName);
        Assert.Null(merchant.LogoKey);
        Assert.Empty(StoredFiles());
    }

    [Fact]
    public async Task Admin_y_anonimo_no_entran()
    {
        await CreateConfirmedUserAsync("admin@prueba.mx", Roles.Admin);
        var admin = CreateClient();
        await LoginAsync(admin, "admin@prueba.mx");

        var asAdmin = await admin.GetAsync(Url);
        Assert.Equal(HttpStatusCode.Redirect, asAdmin.StatusCode);
        Assert.StartsWith("/Identity/Account/AccessDenied", Location(asAdmin));

        var anonymous = await CreateClient().GetAsync(Url);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("/Identity/Account/Login", Location(anonymous));
    }

    // --- Imágenes ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Logo_se_guarda_en_PNG_con_transparencia_y_se_sirve_con_cache()
    {
        var client = await MerchantClientAsync(Email);

        var response = await UploadAsync(client, LogoUrl, MerchantImageProcessorTests.HalfTransparentPng(2000, 1000), "logo.png");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var merchant = await WithDbAsync(db => MerchantOfAsync(db, Email));
        Assert.StartsWith($"merchants/{merchant.Id}/logo-", merchant.LogoKey);
        Assert.Equal([$"{merchant.LogoKey}.png"], StoredKeys());

        var image = await client.GetAsync(LogoUrl);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        var cache = image.Headers.CacheControl!;
        Assert.True(cache.Private);
        Assert.Equal(TimeSpan.FromDays(365), cache.MaxAge);
        Assert.Contains(cache.Extensions, e => e.Name == "immutable");
        using var decoded = TestImages.Decode(await image.Content.ReadAsByteArrayAsync());
        Assert.Equal((512, 256), (decoded.Width, decoded.Height));
        Assert.Equal(0, decoded.GetPixel(20, 128).Alpha);

        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, Url));
        Assert.Contains(ProfileMessages.LogoSaved, html);
        Assert.Contains($"src=\"{LogoUrl}?v={MerchantImageRules.Version(merchant.LogoKey!)}\"", html);
    }

    [Fact]
    public async Task Foto_del_local_se_guarda_en_JPEG_sin_EXIF()
    {
        var client = await MerchantClientAsync(Email);

        var response = await UploadAsync(client, PhotoUrl, TestImages.WithExif(TestImages.Solid(2400, 1600), orientation: 6), "local.jpg");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var merchant = await WithDbAsync(db => MerchantOfAsync(db, Email));
        Assert.StartsWith($"merchants/{merchant.Id}/local-", merchant.ShopPhotoKey);
        var bytes = await client.GetByteArrayAsync(PhotoUrl);
        Assert.True(TestImages.IsJpeg(bytes));
        Assert.False(TestImages.HasExif(bytes));
        using var decoded = TestImages.Decode(bytes);
        Assert.Equal((853, 1280), (decoded.Width, decoded.Height)); // enderezada: vertical
    }

    [Fact]
    public async Task Reemplazar_y_quitar_borran_los_archivos_anteriores()
    {
        var client = await MerchantClientAsync(Email);
        await UploadAsync(client, LogoUrl, TestImages.Solid(100, 100, SkiaSharp.SKEncodedImageFormat.Png), "uno.png");
        var first = (await WithDbAsync(db => MerchantOfAsync(db, Email))).LogoKey;

        await UploadAsync(client, LogoUrl, TestImages.Solid(120, 120, SkiaSharp.SKEncodedImageFormat.Png), "dos.png");
        var second = (await WithDbAsync(db => MerchantOfAsync(db, Email))).LogoKey;

        Assert.NotEqual(first, second);
        Assert.Equal([$"{second}.png"], StoredKeys());

        var remove = await PostFormAsync(client, Url, new Dictionary<string, string>(), postUrl: $"{LogoUrl}/quitar");
        Assert.Equal(HttpStatusCode.Redirect, remove.StatusCode);
        Assert.Null((await WithDbAsync(db => MerchantOfAsync(db, Email))).LogoKey);
        Assert.Empty(StoredFiles());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(LogoUrl)).StatusCode);
        Assert.Contains(ProfileMessages.LogoRemoved, WebUtility.HtmlDecode(await GetHtmlAsync(client, Url)));
    }

    [Fact]
    public async Task Archivo_que_no_es_imagen_o_sin_archivo_se_rechaza()
    {
        var client = await MerchantClientAsync(Email);

        var invalid = await UploadAsync(client, LogoUrl, "no soy imagen"u8.ToArray(), "logo.png");
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains(MerchantImageMessages.Invalid, WebUtility.HtmlDecode(await invalid.Content.ReadAsStringAsync()));

        var missing = await PostMultipartAsync(client, Url, new Dictionary<string, string>(), postUrl: PhotoUrl);
        Assert.Equal(HttpStatusCode.OK, missing.StatusCode);
        Assert.Contains(MerchantImageMessages.Required, WebUtility.HtmlDecode(await missing.Content.ReadAsStringAsync()));

        var merchant = await WithDbAsync(db => MerchantOfAsync(db, Email));
        Assert.Null(merchant.LogoKey);
        Assert.Null(merchant.ShopPhotoKey);
        Assert.Empty(StoredFiles());
    }

    [Fact]
    public async Task Imagen_de_mas_de_10_MB_se_rechaza()
    {
        var client = await MerchantClientAsync(Email);

        var response = await UploadAsync(client, PhotoUrl, new byte[MerchantImageRules.MaxUploadBytes + 1], "enorme.jpg");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(MerchantImageMessages.TooLarge, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Empty(StoredFiles());
    }

    [Fact]
    public async Task Sin_imagen_el_GET_responde_404_y_cada_vendedor_ve_solo_la_suya()
    {
        var other = await MerchantClientAsync(OtherEmail);
        await UploadAsync(other, LogoUrl, TestImages.Solid(100, 100, SkiaSharp.SKEncodedImageFormat.Png), "logo.png");
        var client = await MerchantClientAsync(Email);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(LogoUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(PhotoUrl)).StatusCode);
        Assert.DoesNotContain($"src=\"{LogoUrl}?v=", await GetHtmlAsync(client, Url));
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync(LogoUrl)).StatusCode);

        // Quitar la imagen de uno no toca la del otro.
        await PostFormAsync(client, Url, new Dictionary<string, string>(), postUrl: $"{LogoUrl}/quitar");
        Assert.NotNull((await WithDbAsync(db => MerchantOfAsync(db, OtherEmail))).LogoKey);
    }

    // --- Efectos en otras pantallas ----------------------------------------------------------------------------------

    [Fact]
    public async Task Mas_lleva_a_mi_informacion_sin_el_proximamente()
    {
        var client = await MerchantClientAsync(Email);

        var html = await GetHtmlAsync(client, "/app/mas");

        Assert.Contains("data-more-profile", html);
        Assert.Contains("href=\"/app/mi-informacion\"", html);
        Assert.DoesNotContain("Mi información: datos de tu negocio", html);
    }

    [Fact]
    public async Task Inicio_muestra_el_paso_opcional_hasta_completar_la_informacion()
    {
        var client = await MerchantClientAsync(Email);

        var before = await GetHtmlAsync(client, "/app");
        Assert.Contains("data-profile-incomplete", before);
        Assert.Contains("Completa tu información", WebUtility.HtmlDecode(before));

        await SaveAsync(client, "María López", "Tacos Don Ramón", "5512345678", "");

        Assert.DoesNotContain("data-profile-incomplete", await GetHtmlAsync(client, "/app"));
    }

    [Fact]
    public async Task Inicio_no_muestra_el_paso_a_un_vendedor_dado_de_baja()
    {
        var client = await MerchantClientAsync(Email);
        await UpdateMerchantAsync(Email, m => m.Status = MerchantStatus.Disabled);

        Assert.DoesNotContain("data-profile-incomplete", await GetHtmlAsync(client, "/app"));
    }

    [Fact]
    public async Task Mi_cuenta_ya_no_edita_el_celular_y_liga_a_mi_informacion()
    {
        var client = await MerchantClientAsync(Email);

        var html = await GetHtmlAsync(client, "/Identity/Account/Manage");

        Assert.DoesNotContain("Input.PhoneNumber", html);
        Assert.Contains("data-manage-profile-link", html);
        Assert.Contains("href=\"/app/mi-informacion\"", html);
    }

    [Fact]
    public async Task Una_venta_vieja_se_sigue_viendo_bien_al_renombrar_el_negocio()
    {
        var client = await ActiveMerchantAsync(Email);
        // Venta anterior al 2026-11-26: el concepto guardado llevaba el nombre del negocio.
        var old = await InsertSaleAsync(Email, 10m, DateTimeOffset.UtcNow.AddMinutes(-5), concept: "Tacos Don Ramón · 2 tacos al pastor");

        Assert.Equal(HttpStatusCode.Redirect, (await SaveAsync(client, "María López", "Taquería Nueva", "", "")).StatusCode);

        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, $"/app/ventas/{old.Id}"));
        Assert.Contains("data-sale-concept>2 tacos al pastor</p>", html);
        var next = await ChargeAsync(client, concept: "Refresco");
        Assert.Equal("Refresco", next.Concept);
    }

    // --- Ayudas ------------------------------------------------------------------------------------------------------

    private async Task<HttpClient> MerchantClientAsync(string email)
    {
        await CreateMerchantAsync(email);
        var client = CreateClient();
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(client, email)).StatusCode);
        return client;
    }

    private static Task<HttpResponseMessage> SaveAsync(
        HttpClient client, string owner, string business, string phone, string address, string defaultConcept = "") =>
        PostFormAsync(client, Url, new Dictionary<string, string>
        {
            ["Input.OwnerName"] = owner,
            ["Input.BusinessName"] = business,
            ["Input.PhoneNumber"] = phone,
            ["Input.Address"] = address,
            ["Input.DefaultConcept"] = defaultConcept,
        });

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, string url, byte[] content, string fileName) =>
        PostMultipartAsync(client, Url, new Dictionary<string, string>(), [new FormFile("image", fileName, content)], postUrl: url);

    private string[] StoredFiles() =>
        Directory.Exists(Factory.StorageDirectory)
            ? Directory.GetFiles(Factory.StorageDirectory, "*", SearchOption.AllDirectories)
            : [];

    /// <summary>Llaves de los archivos guardados (rutas relativas con <c>/</c>).</summary>
    private string[] StoredKeys() =>
        StoredFiles().Select(f => Path.GetRelativePath(Factory.StorageDirectory, f).Replace('\\', '/')).Order().ToArray();
}
