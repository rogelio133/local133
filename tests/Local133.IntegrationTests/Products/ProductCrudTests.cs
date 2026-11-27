using System.Net;
using System.Text.RegularExpressions;
using Local133.Application.Products;
using Local133.Domain.Merchants;
using Local133.Domain.Products;
using Local133.IntegrationTests.Infrastructure;
using Local133.IntegrationTests.Sales;
using Microsoft.EntityFrameworkCore;

namespace Local133.IntegrationTests.Products;

/// <summary>
/// Productos (/app/productos): alta, edición, foto (cámara o galería), baja lógica, paginación de 20, búsqueda y
/// aislamiento entre vendedores.
/// </summary>
public partial class ProductCrudTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    private const string ListUrl = "/app/productos";
    private const string NewUrl = "/app/productos/nuevo";
    private const string ResultsUrl = "/app/productos/resultados";

    private static string EditUrl(int id) => $"/app/productos/{id}/editar";

    private static string DeleteUrl(int id) => $"/app/productos/{id}/eliminar";

    private static string PhotoUrl(int id) => $"/app/productos/{id}/foto";

    private static string ThumbnailUrl(int id) => $"/app/productos/{id}/foto/miniatura";

    // --- Lista --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Sin_productos_muestra_el_estado_vacio()
    {
        var client = await MerchantClientAsync(Email);

        var html = await GetHtmlAsync(client, ListUrl);

        Assert.Contains("data-products-empty", html);
        Assert.Contains("Agregar mi primer producto", html);
        Assert.DoesNotContain("data-product-search", html);
    }

    [Fact]
    public async Task Mas_tiene_la_liga_a_productos()
    {
        var client = await MerchantClientAsync(Email);

        var html = await GetHtmlAsync(client, "/app/mas");

        Assert.Contains("href=\"/app/productos\"", html);
    }

    [Fact]
    public async Task Pagina_de_20_en_20_ordenado_por_nombre()
    {
        var client = await MerchantClientAsync(Email);
        await SeedProductsAsync(Email, Enumerable.Range(1, 45).Select(i => $"Producto {i:D2}").Reverse());

        var first = await GetHtmlAsync(client, ListUrl);
        var second = await GetHtmlAsync(client, $"{ListUrl}?pagina=2");
        var third = await GetHtmlAsync(client, $"{ListUrl}?pagina=3");

        Assert.Equal(Names(1, 20), ProductNames(first));
        Assert.Equal(Names(21, 40), ProductNames(second));
        Assert.Equal(Names(41, 45), ProductNames(third));
        Assert.Contains("Página 1 de 3", first);
        Assert.Contains("45 productos", first);
        Assert.Contains("href=\"/app/productos?pagina=2\"", first);
        Assert.DoesNotContain("data-pager-previous", first);
        Assert.Contains("href=\"/app/productos\"", second); // «Anterior» de la página 2 va a la 1 sin parámetro
        Assert.DoesNotContain("data-pager-next", third);
    }

    [Theory]
    [InlineData("0", 1)]
    [InlineData("-5", 1)]
    [InlineData("99", 2)]
    [InlineData("abc", 1)]
    public async Task Una_pagina_fuera_de_rango_se_ajusta(string page, int expected)
    {
        var client = await MerchantClientAsync(Email);
        await SeedProductsAsync(Email, Enumerable.Range(1, 25).Select(i => $"Producto {i:D2}"));

        var html = await GetHtmlAsync(client, $"{ListUrl}?pagina={page}");

        Assert.Contains($"Página {expected} de 2", html);
        Assert.Equal(expected == 1 ? 20 : 5, ProductNames(html).Count);
    }

    [Fact]
    public async Task Con_20_o_menos_no_hay_paginacion()
    {
        var client = await MerchantClientAsync(Email);
        await SeedProductsAsync(Email, Enumerable.Range(1, 20).Select(i => $"Producto {i:D2}"));

        var html = await GetHtmlAsync(client, ListUrl);

        Assert.Equal(20, ProductNames(html).Count);
        Assert.DoesNotContain("data-pager", html);
    }

    [Fact]
    public async Task Busca_por_nombre_y_conserva_la_busqueda_al_paginar()
    {
        var client = await MerchantClientAsync(Email);
        await SeedProductsAsync(Email,
            Enumerable.Range(1, 25).Select(i => $"Agua de sabor {i:D2}").Concat(["Taco al pastor", "Gringa"]));

        var first = await GetHtmlAsync(client, $"{ListUrl}?q=%20agua%20");
        var second = await GetHtmlAsync(client, $"{ListUrl}?q=agua&pagina=2");
        var none = await GetHtmlAsync(client, $"{ListUrl}?q=pozole");

        Assert.Equal(20, ProductNames(first).Count);
        Assert.All(ProductNames(first), n => Assert.StartsWith("Agua de sabor", n));
        Assert.Contains("25 resultados", first);
        Assert.Contains("href=\"/app/productos?q=agua&amp;pagina=2\"", first);
        Assert.Equal(5, ProductNames(second).Count);
        Assert.Contains("data-products-no-results", none);
        Assert.Contains("data-clear-search", none);
    }

    [Fact]
    public async Task La_lista_trae_el_buscador_asincrono()
    {
        var client = await MerchantClientAsync(Email);
        await SeedProductsAsync(Email, ["Gringa"]);

        var html = await GetHtmlAsync(client, ListUrl);

        Assert.Contains($"data-results-url=\"{ResultsUrl}\"", html);
        Assert.Contains("data-product-results", html);
        Assert.Matches("src=\"/js/product-search[^\"]*\\.js\"", html);
    }

    [Fact]
    public async Task Resultados_devuelve_solo_el_fragmento_con_busqueda_y_pagina()
    {
        var client = await MerchantClientAsync(Email);
        await SeedProductsAsync(Email,
            Enumerable.Range(1, 25).Select(i => $"Agua de sabor {i:D2}").Concat(["Taco al pastor", "Gringa"]));

        var response = await client.GetAsync($"{ResultsUrl}?q=%20agua%20&pagina=2");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        Assert.DoesNotContain("<html", html);
        Assert.DoesNotContain("data-product-search", html);
        Assert.Equal(Enumerable.Range(21, 5).Select(i => $"Agua de sabor {i:D2}"), ProductNames(html));
        Assert.Contains("25 resultados", html);
        Assert.Contains("Página 2 de 2", html);
        // Los enlaces son de la lista completa (funcionan sin JS; product-search.js los intercepta).
        Assert.Contains("href=\"/app/productos?q=agua\"", html);
    }

    [Fact]
    public async Task Resultados_sin_coincidencias_ofrece_ver_todos()
    {
        var client = await MerchantClientAsync(Email);
        await SeedProductsAsync(Email, ["Gringa"]);

        var html = await GetHtmlAsync(client, $"{ResultsUrl}?q=pozole");

        Assert.Contains("data-products-no-results", html);
        Assert.Contains("data-clear-search", html);
        Assert.Contains("href=\"/app/productos\"", html);
    }

    [Fact]
    public async Task Resultados_sin_busqueda_trae_la_primera_pagina()
    {
        var client = await MerchantClientAsync(Email);
        await SeedProductsAsync(Email, Enumerable.Range(1, 45).Select(i => $"Producto {i:D2}"));

        var html = await GetHtmlAsync(client, ResultsUrl);

        Assert.Equal(Names(1, 20), ProductNames(html));
        Assert.Contains("45 productos", html);
        Assert.Contains("href=\"/app/productos?pagina=2\"", html);
    }

    // --- Alta -----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Alta_sin_foto_desde_cuenta_bancaria_pendiente()
    {
        // Recién registrado (PendingBankAccount): ya puede armar su catálogo.
        var client = await MerchantClientAsync(Email);

        var response = await PostProductAsync(client, NewUrl, "  Taco al pastor ", "25.5", "Con piña");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(ListUrl, Location(response));
        var product = Assert.Single(await ProductsAsync());
        Assert.Equal("Taco al pastor", product.Name);
        Assert.Equal(25.5m, product.Price);
        Assert.Equal("Con piña", product.Description);
        Assert.Null(product.PhotoKey);
        Assert.Equal(await MerchantIdAsync(Email), product.MerchantId);

        var html = await GetHtmlAsync(client, ListUrl);
        Assert.Contains("Agregamos «Taco al pastor» a tus productos.", html);
        Assert.Contains("$25.50", html);
        Assert.Equal(["Taco al pastor"], ProductNames(html));
    }

    [Theory]
    [InlineData("camara.jpg")]
    [InlineData("galeria.png")]
    public async Task Alta_con_foto_guarda_foto_y_miniatura(string fileName)
    {
        var client = await MerchantClientAsync(Email);
        var image = fileName.EndsWith(".png")
            ? TestImages.Solid(2400, 1600, SkiaSharp.SKEncodedImageFormat.Png)
            : TestImages.WithExif(TestImages.Solid(2400, 1600), orientation: 6);

        var response = await PostProductAsync(client, NewUrl, "Gringa", "45", photo: new FormFile("Input.Photo", fileName, image));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var product = Assert.Single(await ProductsAsync());
        Assert.StartsWith($"products/{product.MerchantId}/", product.PhotoKey);
        Assert.Equal(2, StoredFiles().Length);

        var photo = await client.GetAsync(PhotoUrl(product.Id));
        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
        Assert.Equal("image/jpeg", photo.Content.Headers.ContentType?.MediaType);
        var cache = photo.Headers.CacheControl!;
        Assert.True(cache.Private);
        Assert.Equal(TimeSpan.FromDays(365), cache.MaxAge);
        Assert.Contains(cache.Extensions, e => e.Name == "immutable");
        var bytes = await photo.Content.ReadAsByteArrayAsync();
        Assert.False(TestImages.HasExif(bytes));
        using var big = TestImages.Decode(bytes);
        Assert.Equal(ProductPhotoRules.PhotoMaxSize, Math.Max(big.Width, big.Height));

        using var thumb = TestImages.Decode(await client.GetByteArrayAsync(ThumbnailUrl(product.Id)));
        Assert.Equal((ProductPhotoRules.ThumbnailSize, ProductPhotoRules.ThumbnailSize), (thumb.Width, thumb.Height));

        var html = await GetHtmlAsync(client, ListUrl);
        Assert.Contains($"src=\"{ThumbnailUrl(product.Id)}?v=", html);
    }

    [Fact]
    public async Task Alta_con_datos_invalidos_no_guarda_nada()
    {
        var client = await MerchantClientAsync(Email);

        var response = await PostProductAsync(client, NewUrl, "Taco 🌮", "12.345", new string('x', 501),
            new FormFile("Input.Photo", "foto.jpg", TestImages.Solid(100, 100)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains(ProductMessages.PriceInvalid, html);
        Assert.Contains(ProductMessages.NameInvalid, html);
        Assert.Contains(ProductMessages.DescriptionTooLong, html);
        Assert.Contains("data-photo-reselect", html);
        Assert.Empty(await ProductsAsync());
        Assert.Empty(StoredFiles());
    }

    [Fact]
    public async Task Nombre_con_caracteres_no_permitidos_se_rechaza()
    {
        var client = await MerchantClientAsync(Email);

        var response = await PostProductAsync(client, NewUrl, "Taco @ casa", "10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("estos signos", await response.Content.ReadAsStringAsync());
        Assert.Empty(await ProductsAsync());
    }

    [Fact]
    public async Task Archivo_que_no_es_imagen_se_rechaza()
    {
        var client = await MerchantClientAsync(Email);

        var response = await PostProductAsync(client, NewUrl, "Gringa", "45",
            photo: new FormFile("Input.Photo", "foto.jpg", "no soy una foto"u8.ToArray()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains(ProductMessages.PhotoInvalid, html);
        // El error es la foto misma: no se agrega «Corrige los datos y vuelve a elegir la foto».
        Assert.DoesNotContain("data-photo-reselect", html);
        Assert.Empty(await ProductsAsync());
        Assert.Empty(StoredFiles());
    }

    [Fact]
    public async Task Foto_de_mas_de_10_MB_se_rechaza()
    {
        var client = await MerchantClientAsync(Email);
        var tooBig = new byte[ProductPhotoRules.MaxUploadBytes + 1];

        var response = await PostProductAsync(client, NewUrl, "Gringa", "45", photo: new FormFile("Input.Photo", "enorme.jpg", tooBig));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(ProductMessages.PhotoTooLarge, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Empty(await ProductsAsync());
    }

    [Fact]
    public async Task Peticion_de_mas_del_limite_se_rechaza_sin_error_500()
    {
        var client = await MerchantClientAsync(Email);
        var huge = new byte[ProductPhotoRules.MaxRequestBytes + 1024];

        var response = await PostProductAsync(client, NewUrl, "Gringa", "45", photo: new FormFile("Input.Photo", "enorme.jpg", huge));

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge });
        Assert.Empty(await ProductsAsync());
    }

    [Fact]
    public async Task Sin_token_antiforgery_responde_400()
    {
        var client = await MerchantClientAsync(Email);

        var response = await client.PostAsync(NewUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Name"] = "Gringa",
            ["Input.Price"] = "45",
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await ProductsAsync());
    }

    // --- Edición --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Edicion_precarga_y_guarda_los_cambios()
    {
        var client = await MerchantClientAsync(Email);
        var id = (await SeedProductsAsync(Email, ["Gringa"])).Single();

        var form = await GetHtmlAsync(client, EditUrl(id));
        var response = await PostProductAsync(client, EditUrl(id), "Gringa grande", "55.00", "Con queso");

        Assert.Contains("value=\"Gringa\"", form);
        Assert.Contains("value=\"10.00\"", form);
        Assert.Contains("data-delete-product", form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var product = await ProductAsync(id);
        Assert.Equal(("Gringa grande", 55m, "Con queso"), (product.Name, product.Price, product.Description));
        Assert.Contains("Guardamos los cambios de «Gringa grande».", await GetHtmlAsync(client, ListUrl));
    }

    [Fact]
    public async Task Reemplazar_la_foto_borra_la_anterior()
    {
        var client = await MerchantClientAsync(Email);
        await PostProductAsync(client, NewUrl, "Gringa", "45", photo: new FormFile("Input.Photo", "a.jpg", TestImages.Solid(500, 500)));
        var product = Assert.Single(await ProductsAsync());
        var oldKey = product.PhotoKey!;

        var response = await PostProductAsync(client, EditUrl(product.Id), "Gringa", "45",
            photo: new FormFile("Input.Photo", "b.webp", TestImages.Solid(500, 500, SkiaSharp.SKEncodedImageFormat.Webp), "image/webp"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var newKey = (await ProductAsync(product.Id)).PhotoKey!;
        Assert.NotEqual(oldKey, newKey);
        var files = StoredFiles();
        Assert.Equal(2, files.Length);
        Assert.All(files, f => Assert.StartsWith(newKey[(newKey.LastIndexOf('/') + 1)..], Path.GetFileName(f)));
    }

    [Fact]
    public async Task Quitar_la_foto_borra_sus_archivos()
    {
        var client = await MerchantClientAsync(Email);
        await PostProductAsync(client, NewUrl, "Gringa", "45", photo: new FormFile("Input.Photo", "a.jpg", TestImages.Solid(500, 500)));
        var product = Assert.Single(await ProductsAsync());

        var response = await PostProductAsync(client, EditUrl(product.Id), "Gringa", "45", removePhoto: true);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null((await ProductAsync(product.Id)).PhotoKey);
        Assert.Empty(StoredFiles());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(PhotoUrl(product.Id))).StatusCode);
    }

    [Fact]
    public async Task Sin_foto_la_foto_responde_404()
    {
        var client = await MerchantClientAsync(Email);
        var id = (await SeedProductsAsync(Email, ["Gringa"])).Single();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(PhotoUrl(id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(ThumbnailUrl(id))).StatusCode);
    }

    // --- Baja lógica ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Eliminar_es_baja_logica()
    {
        var client = await MerchantClientAsync(Email);
        await PostProductAsync(client, NewUrl, "Gringa", "45", photo: new FormFile("Input.Photo", "a.jpg", TestImages.Solid(300, 300)));
        var id = Assert.Single(await ProductsAsync()).Id;

        var response = await PostFormAsync(client, EditUrl(id), new Dictionary<string, string>(), DeleteUrl(id));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(ListUrl, Location(response));
        var product = await ProductAsync(id);
        Assert.True(product.IsDeleted);
        Assert.NotNull(product.DeletedAt);
        Assert.Equal(2, StoredFiles().Length); // la foto se conserva

        var html = await GetHtmlAsync(client, ListUrl);
        Assert.Contains("Eliminamos «Gringa» de tus productos.", html);
        Assert.Empty(ProductNames(html));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(EditUrl(id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(PhotoUrl(id))).StatusCode);

        var again = await PostFormAsync(client, TokenPage, new Dictionary<string, string>(), DeleteUrl(id));
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        var edit = await PostMultipartAsync(client, TokenPage, ProductFields("Otra", "1"), postUrl: EditUrl(id));
        Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);
    }

    // --- Aislamiento y permisos ------------------------------------------------------------------------------------------

    [Fact]
    public async Task No_ve_ni_modifica_productos_de_otro_vendedor()
    {
        var other = await MerchantClientAsync(OtherEmail);
        await PostProductAsync(other, NewUrl, "Pozole", "90", photo: new FormFile("Input.Photo", "a.jpg", TestImages.Solid(300, 300)));
        var foreign = Assert.Single(await ProductsAsync());
        var client = await MerchantClientAsync(Email);
        await SeedProductsAsync(Email, ["Gringa"]);

        var list = await GetHtmlAsync(client, $"{ListUrl}?q=Pozole");
        Assert.Contains("data-products-no-results", list);
        Assert.Contains("data-products-no-results", await GetHtmlAsync(client, $"{ResultsUrl}?q=Pozole"));
        Assert.Equal(["Gringa"], ProductNames(await GetHtmlAsync(client, ListUrl)));
        Assert.Equal(["Gringa"], ProductNames(await GetHtmlAsync(client, ResultsUrl)));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(EditUrl(foreign.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(PhotoUrl(foreign.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(ThumbnailUrl(foreign.Id))).StatusCode);
        var edit = await PostMultipartAsync(client, TokenPage, ProductFields("Robado", "1"), postUrl: EditUrl(foreign.Id));
        Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);
        var delete = await PostFormAsync(client, TokenPage, new Dictionary<string, string>(), DeleteUrl(foreign.Id));
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

        var unchanged = await ProductAsync(foreign.Id);
        Assert.Equal(("Pozole", false), (unchanged.Name, unchanged.IsDeleted));
        Assert.Equal(2, StoredFiles().Length);
    }

    [Fact]
    public async Task Vendedor_dado_de_baja_solo_consulta()
    {
        var client = await MerchantClientAsync(Email);
        var id = (await SeedProductsAsync(Email, ["Gringa"])).Single();
        await UpdateMerchantAsync(Email, m => m.Status = MerchantStatus.Disabled);

        var list = await GetHtmlAsync(client, ListUrl);
        var newPage = await client.GetAsync(NewUrl);
        var editPage = await client.GetAsync(EditUrl(id));
        var create = await PostMultipartAsync(client, TokenPage, ProductFields("Taco", "10"), postUrl: NewUrl);
        var delete = await PostFormAsync(client, TokenPage, new Dictionary<string, string>(), DeleteUrl(id));

        Assert.Contains("data-products-readonly", list);
        Assert.DoesNotContain(EditUrl(id), list);
        Assert.DoesNotContain("data-new-product", list);
        foreach (var response in new[] { newPage, editPage, create, delete })
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal(ListUrl, Location(response));
        }

        Assert.Equal(["Gringa"], (await ProductsAsync()).Select(p => p.Name));
        Assert.False((await ProductAsync(id)).IsDeleted);
        Assert.Contains(ProductMessages.NotAllowed, WebUtility.HtmlDecode(await GetHtmlAsync(client, ListUrl)));
    }

    [Fact]
    public async Task Basico_deja_el_catalogo_en_solo_lectura()
    {
        var client = await MerchantClientAsync(Email);
        var id = (await SeedProductsAsync(Email, ["Gringa", "Taco"]))[0];
        await SeedBasicPlanAsync(Email);

        var list = WebUtility.HtmlDecode(await GetHtmlAsync(client, ListUrl));
        var newPage = await client.GetAsync(NewUrl);
        var editPage = await client.GetAsync(EditUrl(id));
        var create = await PostMultipartAsync(client, TokenPage, ProductFields("Pozole", "10"), postUrl: NewUrl);
        var update = await PostMultipartAsync(client, TokenPage, ProductFields("Gringa grande", "60"), postUrl: EditUrl(id));
        var delete = await PostFormAsync(client, TokenPage, new Dictionary<string, string>(), DeleteUrl(id));

        Assert.Contains("data-products-readonly-plan=\"basic\"", list);
        Assert.Contains("data-pro-required", list);
        Assert.Contains("Tu catálogo de 2 productos te espera en Pro.", list);
        Assert.DoesNotContain("Tu cuenta está dada de baja", list);
        Assert.DoesNotContain(EditUrl(id), list);
        Assert.DoesNotContain("data-new-product", list);
        foreach (var response in new[] { newPage, editPage, create, update, delete })
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal(ListUrl, Location(response));
        }

        Assert.Equal(["Gringa", "Taco"], (await ProductsAsync()).Select(p => p.Name).Order());
        Assert.False((await ProductAsync(id)).IsDeleted);
        Assert.Contains(ProductMessages.PlanRequired, WebUtility.HtmlDecode(await GetHtmlAsync(client, ListUrl)));
    }

    [Fact]
    public async Task En_la_prueba_el_catalogo_se_edita()
    {
        var client = await MerchantClientAsync(Email);
        await SeedTrialAsync(Email);

        var response = await PostProductAsync(client, NewUrl, "Pozole", "80");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(["Pozole"], (await ProductsAsync()).Select(p => p.Name));
        Assert.DoesNotContain("data-products-readonly", await GetHtmlAsync(client, ListUrl));
    }

    [Fact]
    public async Task Admin_no_entra_a_productos()
    {
        await CreateConfirmedUserAsync("admin@prueba.mx", Application.Auth.Roles.Admin);
        var client = CreateClient();
        await LoginAsync(client, "admin@prueba.mx");

        foreach (var url in new[] { ListUrl, ResultsUrl })
        {
            var response = await client.GetAsync(url);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("/Identity/Account/AccessDenied", Location(response));
        }
    }

    [Theory]
    [InlineData(ListUrl)]
    [InlineData(ResultsUrl)]
    public async Task Sin_sesion_redirige_al_login(string url)
    {
        var response = await CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Identity/Account/Login", Location(response));
    }

    // --- Ayudas --------------------------------------------------------------------------------------------------------

    private async Task<HttpClient> MerchantClientAsync(string email)
    {
        await CreateMerchantAsync(email);
        var client = CreateClient();
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(client, email)).StatusCode);
        return client;
    }

    private static Dictionary<string, string> ProductFields(string name, string price, string? description = null, bool removePhoto = false)
    {
        var fields = new Dictionary<string, string> { ["Input.Name"] = name, ["Input.Price"] = price };
        if (description is not null)
        {
            fields["Input.Description"] = description;
        }

        if (removePhoto)
        {
            fields["Input.RemovePhoto"] = "true";
        }

        return fields;
    }

    private static Task<HttpResponseMessage> PostProductAsync(
        HttpClient client, string url, string name, string price, string? description = null, FormFile? photo = null,
        bool removePhoto = false) =>
        PostMultipartAsync(client, url, ProductFields(name, price, description, removePhoto), photo is null ? null : [photo]);

    /// <summary>Productos insertados directo en la base (precio $10); devuelve sus ids en el orden dado.</summary>
    private async Task<List<int>> SeedProductsAsync(string email, IEnumerable<string> names)
    {
        var merchantId = await MerchantIdAsync(email);
        return await WithDbAsync(async db =>
        {
            var products = names.Select(n => Product.Create(merchantId, n, null, 10m, DateTimeOffset.UtcNow)).ToList();
            db.Products.AddRange(products);
            await db.SaveChangesAsync();
            return products.Select(p => p.Id).ToList();
        });
    }

    private Task<List<Product>> ProductsAsync() =>
        WithDbAsync(db => db.Products.AsNoTracking().OrderBy(p => p.Id).ToListAsync());

    private Task<Product> ProductAsync(int id) =>
        WithDbAsync(db => db.Products.AsNoTracking().SingleAsync(p => p.Id == id));

    private string[] StoredFiles() =>
        Directory.Exists(Factory.StorageDirectory)
            ? Directory.GetFiles(Factory.StorageDirectory, "*", SearchOption.AllDirectories)
            : [];

    private static List<string> Names(int from, int to) =>
        Enumerable.Range(from, to - from + 1).Select(i => $"Producto {i:D2}").ToList();

    private static List<string> ProductNames(string html) =>
        ProductNameRegex().Matches(html).Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();

    [GeneratedRegex("data-product-name>([^<]+)</span>")]
    private static partial Regex ProductNameRegex();
}
