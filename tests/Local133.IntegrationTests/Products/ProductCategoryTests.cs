using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Local133.Application.Products;
using Local133.Domain.Merchants;
using Local133.Domain.Products;
using Local133.IntegrationTests.Infrastructure;
using Local133.IntegrationTests.Sales;
using Local133.Web.PublicPages;
using Microsoft.EntityFrameworkCore;

namespace Local133.IntegrationTests.Products;

/// <summary>
/// Categorías de productos (ajustes 2026-11-26, punto 14): elegir o crear una al dar de alta o editar un producto,
/// pantalla «Categorías» (agregar, renombrar, borrar y ordenar), filtros en Productos y en el carrito, catálogo
/// público agrupado, permisos del catálogo y aislamiento entre vendedores.
/// </summary>
public partial class ProductCategoryTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    private const string ProductsUrl = "/app/productos";
    private const string NewProductUrl = "/app/productos/nuevo";
    private const string ResultsUrl = "/app/productos/resultados";
    private const string CategoriesUrl = "/app/productos/categorias";
    private const string OrderUrl = "/app/productos/categorias/orden";
    private const string CartUrl = "/app/cobrar/productos";
    private const string CartCatalogUrl = "/app/cobrar/productos/catalogo";

    private static string EditUrl(int id) => $"/app/productos/{id}/editar";

    // --- Formulario del producto ------------------------------------------------------------------------------------

    [Fact]
    public async Task El_formulario_ofrece_las_categorias_en_su_orden_y_crear_una()
    {
        var client = await MerchantClientAsync(Email);
        await SeedCategoriesAsync(Email, "Tacos", "Bebidas");
        await MerchantClientAsync(OtherEmail);
        await SeedCategoriesAsync(OtherEmail, "Ajena");

        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, NewProductUrl));

        Assert.Contains("data-category-select", html);
        Assert.Equal(["Sin categoría", "Tacos", "Bebidas", "+ Nueva categoría…"], OptionTexts(html));
        Assert.Contains("data-new-category-name", html);
        Assert.DoesNotContain("Ajena", html);
    }

    [Fact]
    public async Task Alta_con_categoria_nueva_la_crea_al_final_y_la_asigna()
    {
        var client = await MerchantClientAsync(Email);
        await SeedCategoriesAsync(Email, "Tacos");

        var response = await PostProductAsync(client, NewProductUrl, "Agua de horchata", "30",
            category: ProductInputNew, newCategory: "  Aguas   frescas ");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var categories = await CategoriesAsync();
        Assert.Equal([("Tacos", 0), ("Aguas frescas", 1)], categories.Select(c => (c.Name, c.SortOrder)));
        var product = Assert.Single(await ProductsAsync());
        Assert.Equal(categories[1].Id, product.CategoryId);
    }

    [Fact]
    public async Task Sin_JS_el_nombre_nuevo_gana_sobre_la_categoria_elegida()
    {
        var client = await MerchantClientAsync(Email);
        var tacos = (await SeedCategoriesAsync(Email, "Tacos"))[0];

        await PostProductAsync(client, NewProductUrl, "Agua", "30", category: tacos.ToString(), newCategory: "Bebidas");

        var product = Assert.Single(await ProductsAsync());
        Assert.Equal("Bebidas", (await CategoriesAsync()).Single(c => c.Id == product.CategoryId).Name);
    }

    [Fact]
    public async Task Alta_con_categoria_existente()
    {
        var client = await MerchantClientAsync(Email);
        var bebidas = (await SeedCategoriesAsync(Email, "Tacos", "Bebidas"))[1];

        await PostProductAsync(client, NewProductUrl, "Agua", "30", category: bebidas.ToString());

        Assert.Equal(bebidas, Assert.Single(await ProductsAsync()).CategoryId);
        Assert.Equal(2, (await CategoriesAsync()).Count);
    }

    [Fact]
    public async Task Un_nombre_repetido_con_otras_mayusculas_reutiliza_la_categoria()
    {
        var client = await MerchantClientAsync(Email);
        var bebidas = (await SeedCategoriesAsync(Email, "Bebidas"))[0];

        await PostProductAsync(client, NewProductUrl, "Agua", "30", category: ProductInputNew, newCategory: "BEBIDAS");

        Assert.Equal(bebidas, Assert.Single(await ProductsAsync()).CategoryId);
        Assert.Equal(["Bebidas"], (await CategoriesAsync()).Select(c => c.Name));
    }

    [Fact]
    public async Task Una_categoria_de_otro_vendedor_responde_404()
    {
        await MerchantClientAsync(OtherEmail);
        var foreign = (await SeedCategoriesAsync(OtherEmail, "Ajena"))[0];
        var client = await MerchantClientAsync(Email);
        var productId = await CreateProductAsync(Email, "Taco", 25m);

        var create = await PostProductAsync(client, NewProductUrl, "Agua", "30", category: foreign.ToString());
        var update = await PostProductAsync(client, EditUrl(productId), "Taco", "25", category: foreign.ToString());

        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(["Taco"], (await ProductsAsync()).Select(p => p.Name));
        Assert.All(await ProductsAsync(), p => Assert.Null(p.CategoryId));
    }

    [Fact]
    public async Task Nueva_sin_nombre_o_con_un_nombre_invalido_no_guarda_nada()
    {
        var client = await MerchantClientAsync(Email);

        var empty = await PostProductAsync(client, NewProductUrl, "Agua", "30", category: ProductInputNew, newCategory: " ");
        var invalid = await PostProductAsync(client, NewProductUrl, "Agua", "30", category: ProductInputNew, newCategory: "Bebidas!");

        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Contains(ProductCategoryMessages.NewNameRequired, WebUtility.HtmlDecode(await empty.Content.ReadAsStringAsync()));
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("data-new-category", await invalid.Content.ReadAsStringAsync());
        Assert.Empty(await ProductsAsync());
        Assert.Empty(await CategoriesAsync());
    }

    [Fact]
    public async Task Con_50_categorias_no_se_crea_otra()
    {
        var client = await MerchantClientAsync(Email);
        await SeedCategoriesAsync(Email, Enumerable.Range(1, ProductCategory.MaxPerMerchant).Select(i => $"Cat {i:D2}").ToArray());

        var form = WebUtility.HtmlDecode(await GetHtmlAsync(client, NewProductUrl));
        var response = await PostProductAsync(client, NewProductUrl, "Agua", "30", category: ProductInputNew, newCategory: "Otra más");
        var screen = WebUtility.HtmlDecode(await GetHtmlAsync(client, CategoriesUrl));
        var create = await PostFormAsync(client, CategoriesUrl, new Dictionary<string, string> { ["Input.Name"] = "Otra más" });

        Assert.DoesNotContain("+ Nueva categoría…", form);
        Assert.Contains("data-category-limit", form);
        Assert.Contains(ProductCategoryMessages.LimitReached, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Contains("data-category-limit", screen);
        Assert.DoesNotContain("data-category-create", screen);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        Assert.Contains("data-category-limit", await create.Content.ReadAsStringAsync());
        Assert.Equal(ProductCategory.MaxPerMerchant, (await CategoriesAsync()).Count);
        Assert.Empty(await ProductsAsync());
    }

    [Fact]
    public async Task La_edicion_precarga_la_categoria_y_se_puede_quitar()
    {
        var client = await MerchantClientAsync(Email);
        var tacos = (await SeedCategoriesAsync(Email, "Tacos"))[0];
        var productId = await CreateProductAsync(Email, "Taco", 25m);
        await SetProductCategoryAsync(productId, tacos);

        var html = await GetHtmlAsync(client, EditUrl(productId));
        var response = await PostProductAsync(client, EditUrl(productId), "Taco", "25", category: "");

        Assert.Matches($"<option (selected=\"selected\" value=\"{tacos}\"|value=\"{tacos}\" selected=\"selected\")>Tacos</option>", html);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(Assert.Single(await ProductsAsync()).CategoryId);
    }

    // --- Pantalla «Categorías» ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Productos_liga_a_categorias_y_sin_categorias_no_hay_chips()
    {
        var client = await MerchantClientAsync(Email);
        await CreateProductAsync(Email, "Taco", 25m);

        var list = await GetHtmlAsync(client, ProductsUrl);
        var empty = await GetHtmlAsync(client, CategoriesUrl);

        Assert.Contains($"href=\"{CategoriesUrl}\"", list);
        Assert.DoesNotContain("data-category-filters", list);
        Assert.Contains("data-categories-empty", empty);
        Assert.Contains("data-category-create", empty);
    }

    [Fact]
    public async Task Agregar_una_categoria_y_un_nombre_repetido_se_rechaza()
    {
        var client = await MerchantClientAsync(Email);
        await SeedCategoriesAsync(Email, "Tacos");

        var created = await PostFormAsync(client, CategoriesUrl, new Dictionary<string, string> { ["Input.Name"] = " Bebidas " });
        // El aviso se lee antes del siguiente POST: el GET del token lo consumiría.
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Equal(CategoriesUrl, Location(created));
        Assert.Contains("Agregamos la categoría «Bebidas».", WebUtility.HtmlDecode(await GetHtmlAsync(client, CategoriesUrl)));

        var duplicate = await PostFormAsync(client, CategoriesUrl, new Dictionary<string, string> { ["Input.Name"] = "tacos" });

        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Contains(ProductCategoryMessages.Duplicate("Tacos"), WebUtility.HtmlDecode(await duplicate.Content.ReadAsStringAsync()));
        Assert.Equal(["Tacos", "Bebidas"], (await CategoriesAsync()).Select(c => c.Name));
    }

    [Fact]
    public async Task La_lista_trae_sus_productos_y_las_acciones()
    {
        var client = await MerchantClientAsync(Email);
        var ids = await SeedCategoriesAsync(Email, "Tacos", "Bebidas");
        var taco = await CreateProductAsync(Email, "Taco", 25m);
        var gringa = await CreateProductAsync(Email, "Gringa", 45m);
        await SetProductCategoryAsync(taco, ids[0]);
        await SetProductCategoryAsync(gringa, ids[0]);

        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, CategoriesUrl));

        Assert.Equal(["Tacos", "Bebidas"], CategoryNames(html));
        Assert.Contains("2 productos", html);
        Assert.Contains("Sin productos", html);
        Assert.Contains("data-drag-handle", html);
        Assert.Contains($"data-order-url=\"{OrderUrl}\"", html);
        Assert.Contains("/lib/sortablejs/Sortable.min.", html); // con huella (MapStaticAssets)
        Assert.Contains("/js/product-categories.", html);
        // La primera no sube y la última no baja.
        Assert.Matches(new Regex($"categorias/{ids[0]}/subir\"[\\s\\S]*?disabled=\"disabled\" data-category-up"), html);
        Assert.Matches(new Regex($"categorias/{ids[1]}/bajar\"[\\s\\S]*?disabled=\"disabled\" data-category-down"), html);
    }

    [Fact]
    public async Task Renombrar_y_un_nombre_ocupado_avisa()
    {
        var client = await MerchantClientAsync(Email);
        var ids = await SeedCategoriesAsync(Email, "Tacos", "Bebidas");

        var renamed = await PostFormAsync(client, CategoriesUrl, new Dictionary<string, string> { ["Input.Name"] = "Aguas frescas" },
            $"{CategoriesUrl}/{ids[1]}/renombrar");
        var sameWithCase = await PostFormAsync(client, CategoriesUrl, new Dictionary<string, string> { ["Input.Name"] = "TACOS" },
            $"{CategoriesUrl}/{ids[0]}/renombrar");
        var taken = await PostFormAsync(client, CategoriesUrl, new Dictionary<string, string> { ["Input.Name"] = "aguas frescas" },
            $"{CategoriesUrl}/{ids[0]}/renombrar");

        Assert.All(new[] { renamed, sameWithCase, taken }, r => Assert.Equal(HttpStatusCode.Redirect, r.StatusCode));
        Assert.Equal(["TACOS", "Aguas frescas"], (await CategoriesAsync()).Select(c => c.Name));
        Assert.Contains(ProductCategoryMessages.Duplicate("Aguas frescas"), WebUtility.HtmlDecode(await GetHtmlAsync(client, CategoriesUrl)));
    }

    [Fact]
    public async Task Borrar_deja_los_productos_sin_categoria()
    {
        var client = await MerchantClientAsync(Email);
        var ids = await SeedCategoriesAsync(Email, "Tacos", "Bebidas");
        var taco = await CreateProductAsync(Email, "Taco", 25m);
        var agua = await CreateProductAsync(Email, "Agua", 30m);
        await SetProductCategoryAsync(taco, ids[0]);
        await SetProductCategoryAsync(agua, ids[1]);

        var response = await PostFormAsync(client, CategoriesUrl, new Dictionary<string, string>(), $"{CategoriesUrl}/{ids[0]}/eliminar");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Borramos la categoría «Tacos». Su producto quedó sin categoría.",
            WebUtility.HtmlDecode(await GetHtmlAsync(client, CategoriesUrl)));
        Assert.Equal(["Bebidas"], (await CategoriesAsync()).Select(c => c.Name));
        var products = await ProductsAsync();
        Assert.Null(products.Single(p => p.Id == taco).CategoryId);
        Assert.Equal(ids[1], products.Single(p => p.Id == agua).CategoryId);
        Assert.All(products, p => Assert.False(p.IsDeleted));
    }

    [Fact]
    public async Task Reordenar_con_la_lista_completa_reescribe_el_orden()
    {
        var client = await MerchantClientAsync(Email);
        var ids = await SeedCategoriesAsync(Email, "Tacos", "Bebidas", "Postres");

        var response = await PostOrderAsync(client, ids[2], ids[0], ids[1]);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([("Postres", 0), ("Tacos", 1), ("Bebidas", 2)], (await CategoriesAsync()).Select(c => (c.Name, c.SortOrder)));
    }

    [Fact]
    public async Task Reordenar_con_ids_incompletos_repetidos_o_ajenos_responde_400()
    {
        await MerchantClientAsync(OtherEmail);
        var foreign = (await SeedCategoriesAsync(OtherEmail, "Ajena"))[0];
        var client = await MerchantClientAsync(Email);
        var ids = await SeedCategoriesAsync(Email, "Tacos", "Bebidas");

        foreach (var order in new[] { new[] { ids[1] }, [ids[1], ids[1]], [ids[1], ids[0], foreign], [ids[1], foreign], [] })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await PostOrderAsync(client, order)).StatusCode);
        }

        Assert.Equal(["Tacos", "Bebidas"], (await CategoriesAsync()).Where(c => c.Id != foreign).Select(c => c.Name));
    }

    [Fact]
    public async Task Subir_y_bajar_sin_JS()
    {
        var client = await MerchantClientAsync(Email);
        var ids = await SeedCategoriesAsync(Email, "Tacos", "Bebidas", "Postres");

        var up = await PostFormAsync(client, CategoriesUrl, new Dictionary<string, string>(), $"{CategoriesUrl}/{ids[2]}/subir");
        await PostFormAsync(client, CategoriesUrl, new Dictionary<string, string>(), $"{CategoriesUrl}/{ids[0]}/bajar");
        // En el extremo no se mueve.
        await PostFormAsync(client, CategoriesUrl, new Dictionary<string, string>(), $"{CategoriesUrl}/{ids[1]}/bajar");

        Assert.Equal(HttpStatusCode.Redirect, up.StatusCode);
        Assert.Equal(["Postres", "Tacos", "Bebidas"], (await CategoriesAsync()).Select(c => c.Name));
    }

    [Fact]
    public async Task Las_categorias_de_otro_vendedor_no_se_ven_ni_se_modifican()
    {
        await MerchantClientAsync(OtherEmail);
        var foreign = (await SeedCategoriesAsync(OtherEmail, "Ajena"))[0];
        var client = await MerchantClientAsync(Email);
        await SeedCategoriesAsync(Email, "Tacos");
        await CreateProductAsync(Email, "Taco", 25m);

        var html = await GetHtmlAsync(client, CategoriesUrl);
        var list = await GetHtmlAsync(client, $"{ProductsUrl}?categoria={foreign}");
        Assert.DoesNotContain("Ajena", html);
        Assert.DoesNotContain("Ajena", list);
        Assert.Equal(["Taco"], ProductNames(list)); // filtro ajeno = todas

        foreach (var action in new[] { "renombrar", "eliminar", "subir", "bajar" })
        {
            var response = await PostFormAsync(client, CategoriesUrl, new Dictionary<string, string> { ["Input.Name"] = "Robada" },
                $"{CategoriesUrl}/{foreign}/{action}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        Assert.Equal("Ajena", (await CategoriesAsync()).Single(c => c.Id == foreign).Name);
    }

    [Fact]
    public async Task Basico_ve_sus_categorias_en_solo_lectura()
    {
        var client = await MerchantClientAsync(Email);
        var ids = await SeedCategoriesAsync(Email, "Tacos", "Bebidas");
        await SeedBasicPlanAsync(Email);

        var html = await GetHtmlAsync(client, CategoriesUrl);
        var create = await PostFormAsync(client, CategoriesUrl, new Dictionary<string, string> { ["Input.Name"] = "Postres" });
        var rename = await PostFormAsync(client, CategoriesUrl, new Dictionary<string, string> { ["Input.Name"] = "Otra" },
            $"{CategoriesUrl}/{ids[0]}/renombrar");
        var delete = await PostFormAsync(client, CategoriesUrl, new Dictionary<string, string>(), $"{CategoriesUrl}/{ids[0]}/eliminar");
        var move = await PostFormAsync(client, CategoriesUrl, new Dictionary<string, string>(), $"{CategoriesUrl}/{ids[1]}/subir");
        // El aviso se lee antes del siguiente POST: el GET del token lo consumiría.
        var warning = WebUtility.HtmlDecode(await GetHtmlAsync(client, CategoriesUrl));
        var order = await PostOrderAsync(client, ids[1], ids[0]);

        Assert.Contains("data-products-readonly-plan=\"basic\"", html);
        Assert.Equal(["Tacos", "Bebidas"], CategoryNames(WebUtility.HtmlDecode(html)));
        Assert.DoesNotContain("data-category-create", html);
        Assert.DoesNotContain("data-drag-handle", html);
        Assert.DoesNotContain("data-category-menu", html);
        foreach (var response in new[] { create, rename, delete, move })
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal(CategoriesUrl, Location(response));
        }

        Assert.Equal(HttpStatusCode.Forbidden, order.StatusCode);
        Assert.Contains(ProductMessages.PlanRequired, warning);
        Assert.Equal([("Tacos", 0), ("Bebidas", 1)], (await CategoriesAsync()).Select(c => (c.Name, c.SortOrder)));
    }

    [Fact]
    public async Task Dado_de_baja_ve_sus_categorias_en_solo_lectura()
    {
        var client = await MerchantClientAsync(Email);
        var ids = await SeedCategoriesAsync(Email, "Tacos");
        await UpdateMerchantAsync(Email, m => m.Status = MerchantStatus.Disabled);

        var html = await GetHtmlAsync(client, CategoriesUrl);
        var create = await PostFormAsync(client, CategoriesUrl, new Dictionary<string, string> { ["Input.Name"] = "Postres" });
        var delete = await PostFormAsync(client, CategoriesUrl, new Dictionary<string, string>(), $"{CategoriesUrl}/{ids[0]}/eliminar");

        Assert.Contains("data-products-readonly", html);
        Assert.DoesNotContain("data-category-create", html);
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
        Assert.Contains(ProductMessages.NotAllowed, WebUtility.HtmlDecode(await GetHtmlAsync(client, CategoriesUrl)));
        Assert.Equal(["Tacos"], (await CategoriesAsync()).Select(c => c.Name));
    }

    [Fact]
    public async Task Cada_formulario_de_categorias_trae_su_token_antiforgery()
    {
        // Los helpers de las pruebas toman el token de cualquier formulario; el navegador solo manda el del suyo.
        var client = await MerchantClientAsync(Email);
        await SeedCategoriesAsync(Email, "Tacos", "Bebidas");

        var html = await GetHtmlAsync(client, CategoriesUrl);

        var forms = Regex.Matches(html, "<form[^>]*method=\"post\"[\\s\\S]*?</form>")
            .Select(m => m.Value)
            .Where(f => !f.Contains("logoutForm"))
            .ToList();
        Assert.Equal(7, forms.Count); // agregar, ↑/↓ de cada una, renombrar y borrar
        Assert.All(forms, form => Assert.Contains("name=\"__RequestVerificationToken\"", form));
        Assert.Matches("data-category-order-token>\\s*<input name=\"__RequestVerificationToken\"", html);
    }

    [Fact]
    public async Task Sin_token_antiforgery_responde_400()
    {
        var client = await MerchantClientAsync(Email);
        var ids = await SeedCategoriesAsync(Email, "Tacos", "Bebidas");

        var order = await client.PostAsync(OrderUrl, new FormUrlEncodedContent([new("ids", ids[1].ToString()), new("ids", ids[0].ToString())]));
        var create = await client.PostAsync(CategoriesUrl, new FormUrlEncodedContent([new("Input.Name", "Postres")]));

        Assert.Equal(HttpStatusCode.BadRequest, order.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Equal(["Tacos", "Bebidas"], (await CategoriesAsync()).Select(c => c.Name));
    }

    // --- Filtros ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Productos_filtra_por_categoria_y_lo_combina_con_la_busqueda()
    {
        var client = await MerchantClientAsync(Email);
        var (tacos, bebidas) = await SeedCatalogAsync(Email);

        var all = WebUtility.HtmlDecode(await GetHtmlAsync(client, ProductsUrl));
        var inTacos = await GetHtmlAsync(client, $"{ProductsUrl}?categoria={tacos}");
        var none = await GetHtmlAsync(client, $"{ProductsUrl}?categoria=sin");
        var searched = await GetHtmlAsync(client, $"{ResultsUrl}?q=Taco&categoria={tacos}");
        var empty = WebUtility.HtmlDecode(await GetHtmlAsync(client, $"{ResultsUrl}?q=Agua&categoria={tacos}"));

        Assert.Equal(["Todas", "Tacos", "Bebidas", "Sin categoría"], ChipTexts(all));
        Assert.Contains("data-product-category>Tacos<", all);
        Assert.Equal(["Gringa", "Taco al pastor"], ProductNames(inTacos));
        Assert.Matches($"class=\"category-chip is-active\" href=\"/app/productos\\?categoria={tacos}\" aria-current=\"true\"", inTacos);
        Assert.Contains($"name=\"categoria\" value=\"{tacos}\"", inTacos);
        Assert.DoesNotContain("data-product-category>", inTacos);
        Assert.Equal(["Servilletas"], ProductNames(none));
        Assert.Equal(["Taco al pastor"], ProductNames(searched));
        // Los chips conservan la búsqueda; «Ver todos» conserva la categoría.
        Assert.Contains($"href=\"/app/productos?q=Taco&amp;categoria={bebidas}\"", searched);
        Assert.Contains($"href=\"/app/productos?categoria={tacos}\" data-clear-search", searched);
        Assert.Contains("No encontramos productos con «Agua» en «Tacos».", empty);
    }

    [Fact]
    public async Task Una_categoria_sin_productos_no_es_el_catalogo_vacio()
    {
        var client = await MerchantClientAsync(Email);
        var ids = await SeedCategoriesAsync(Email, "Tacos", "Postres");
        await CreateProductAsync(Email, "Taco", 25m);

        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, $"{ProductsUrl}?categoria={ids[1]}"));

        Assert.DoesNotContain("data-products-empty", html);
        Assert.Contains("No tienes productos en «Postres».", html);
        Assert.Contains("data-product-search", html);
    }

    [Fact]
    public async Task El_carrito_filtra_por_categoria_en_la_pagina_y_en_el_fragmento()
    {
        var client = await ActiveMerchantAsync(Email);
        var (tacos, _) = await SeedCatalogAsync(Email);

        var page = await GetHtmlAsync(client, $"{CartUrl}?categoria={tacos}");
        var fragment = await GetHtmlAsync(client, $"{CartCatalogUrl}?categoria=sin");

        Assert.Contains("data-category-filters", page);
        Assert.Equal(["Gringa", "Taco al pastor"], CartNames(page));
        Assert.Contains($"href=\"/app/cobrar/productos?categoria=sin\"", page);
        Assert.Equal(["Servilletas"], CartNames(fragment));
        Assert.DoesNotContain("<html", fragment);
    }

    // --- Catálogo público -------------------------------------------------------------------------------------------

    [Fact]
    public async Task El_catalogo_publico_agrupa_por_categoria_con_Otros_al_final()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedTrialAsync(Email);
        var ids = await SeedCategoriesAsync(Email, "Tacos", "Bebidas", "Postres"); // Postres sin productos
        await ReorderDirectAsync(ids[1], ids[0], ids[2]); // Bebidas primero
        foreach (var (name, category) in new[] { ("Taco al pastor", ids[0]), ("Gringa", ids[0]), ("Agua", ids[1]) })
        {
            await SetProductCategoryAsync(await CreateProductAsync(Email, name, 25m), category);
        }

        await CreateProductAsync(Email, "Servilletas", 5m);
        await UpdateMerchantAsync(Email, m => m.PublishCatalog("tacos-don-ramon"));

        var storefront = await StorefrontAsync("tacos-don-ramon");
        var searched = await StorefrontAsync("tacos-don-ramon", "a");
        var preview = WebUtility.HtmlDecode(await GetHtmlAsync(client, "/app/tienda/vista-previa"));

        Assert.True(storefront.Grouped);
        Assert.Equal(["Agua", "Gringa", "Taco al pastor", "Servilletas"], storefront.Products.Select(p => p.Name));
        Assert.Equal<string?>(["Bebidas", "Tacos", "Tacos", null], storefront.Products.Select(p => p.Category));
        Assert.True(searched.Grouped);
        Assert.Equal(["Agua", "Gringa", "Taco al pastor", "Servilletas"], searched.Products.Select(p => p.Name));
        Assert.Equal(["Bebidas", "Tacos", "Otros"], Headings(preview));
        Assert.DoesNotContain(">Postres<", preview);
    }

    [Fact]
    public async Task Sin_productos_con_categoria_el_catalogo_va_por_nombre_sin_encabezados()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedTrialAsync(Email);
        await SeedCategoriesAsync(Email, "Tacos"); // existe, pero ningún producto la usa
        await CreateProductAsync(Email, "Taco", 25m);
        await CreateProductAsync(Email, "Agua", 30m);
        await UpdateMerchantAsync(Email, m => m.PublishCatalog("tacos-don-ramon"));

        var storefront = await StorefrontAsync("tacos-don-ramon");
        var preview = await GetHtmlAsync(client, "/app/tienda/vista-previa");

        Assert.False(storefront.Grouped);
        Assert.Equal(["Agua", "Taco"], storefront.Products.Select(p => p.Name));
        Assert.All(storefront.Products, p => Assert.Null(p.Category));
        Assert.DoesNotContain("data-storefront-category", preview);
    }

    [Fact]
    public async Task Con_24_por_pagina_el_encabezado_se_repite_si_la_categoria_continua()
    {
        await ActiveMerchantAsync(Email);
        await SeedTrialAsync(Email);
        var tacos = (await SeedCategoriesAsync(Email, "Tacos"))[0];
        for (var i = 1; i <= 26; i++)
        {
            await SetProductCategoryAsync(await CreateProductAsync(Email, $"Taco {i:D2}", 25m), tacos);
        }

        await CreateProductAsync(Email, "Servilletas", 5m);
        await UpdateMerchantAsync(Email, m => m.PublishCatalog("tacos-don-ramon"));

        var second = await StorefrontAsync("tacos-don-ramon", page: 2);

        Assert.Equal(["Taco 25", "Taco 26", "Servilletas"], second.Products.Select(p => p.Name));
        Assert.Equal(["Tacos", "Otros"], StorefrontGroups.Of(second.Products, second.Grouped, p => p.Category).Select(g => g.Heading));
    }

    // --- Ayudas --------------------------------------------------------------------------------------------------------

    private const string ProductInputNew = "nueva";

    private async Task<HttpClient> MerchantClientAsync(string email)
    {
        await CreateMerchantAsync(email);
        var client = CreateClient();
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(client, email)).StatusCode);
        return client;
    }

    private static Task<HttpResponseMessage> PostProductAsync(
        HttpClient client, string url, string name, string price, string? category = null, string? newCategory = null)
    {
        var fields = new Dictionary<string, string> { ["Input.Name"] = name, ["Input.Price"] = price };
        if (category is not null)
        {
            fields["Input.Category"] = category;
        }

        if (newCategory is not null)
        {
            fields["Input.NewCategoryName"] = newCategory;
        }

        return PostMultipartAsync(client, TokenPage, fields, postUrl: url);
    }

    private static async Task<HttpResponseMessage> PostOrderAsync(HttpClient client, params int[] ids)
    {
        var token = await GetAntiforgeryTokenAsync(client, CategoriesUrl);
        var fields = ids.Select(id => new KeyValuePair<string, string>("ids", id.ToString()))
            .Append(new("__RequestVerificationToken", token));
        return await client.PostAsync(OrderUrl, new FormUrlEncodedContent(fields));
    }

    /// <summary>Categorías insertadas directo en la base, en el orden dado; devuelve sus ids.</summary>
    private async Task<List<int>> SeedCategoriesAsync(string email, params string[] names)
    {
        var merchantId = await MerchantIdAsync(email);
        return await WithDbAsync(async db =>
        {
            var categories = names.Select((n, i) => ProductCategory.Create(merchantId, n, i, DateTimeOffset.UtcNow)).ToList();
            db.ProductCategories.AddRange(categories);
            await db.SaveChangesAsync();
            return categories.Select(c => c.Id).ToList();
        });
    }

    /// <summary>Tacos (Taco al pastor, Gringa), Bebidas (Agua de horchata) y Servilletas sin categoría.</summary>
    private async Task<(int Tacos, int Bebidas)> SeedCatalogAsync(string email)
    {
        var ids = await SeedCategoriesAsync(email, "Tacos", "Bebidas");
        await SetProductCategoryAsync(await CreateProductAsync(email, "Taco al pastor", 25m), ids[0]);
        await SetProductCategoryAsync(await CreateProductAsync(email, "Gringa", 45m), ids[0]);
        await SetProductCategoryAsync(await CreateProductAsync(email, "Agua de horchata", 30m), ids[1]);
        await CreateProductAsync(email, "Servilletas", 5m);
        return (ids[0], ids[1]);
    }

    private Task SetProductCategoryAsync(int productId, int categoryId) => WithDbAsync(async db =>
    {
        var product = await db.Products.IgnoreQueryFilters().SingleAsync(p => p.Id == productId);
        product.CategoryId = categoryId;
        await db.SaveChangesAsync();
    });

    private Task ReorderDirectAsync(params int[] ids) => WithDbAsync(async db =>
    {
        var categories = await db.ProductCategories.IgnoreQueryFilters().Where(c => ids.Contains(c.Id)).ToListAsync();
        for (var i = 0; i < ids.Length; i++)
        {
            categories.Single(c => c.Id == ids[i]).SortOrder = i;
        }

        await db.SaveChangesAsync();
    });

    private Task<List<ProductCategory>> CategoriesAsync() =>
        WithDbAsync(db => db.ProductCategories.IgnoreQueryFilters().AsNoTracking()
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToListAsync());

    private Task<List<Product>> ProductsAsync() =>
        WithDbAsync(db => db.Products.IgnoreQueryFilters().AsNoTracking().OrderBy(p => p.Id).ToListAsync());

    private async Task<PublicStorefront> StorefrontAsync(string slug, string? q = null, int? page = null)
    {
        var json = await LandingClient().GetStringAsync(PublicPagesApi.Storefront(slug, q, page));
        return JsonSerializer.Deserialize<PublicStorefront>(json, JsonSerializerOptions.Web)!;
    }

    private static List<string> OptionTexts(string html) =>
        OptionRegex().Matches(SelectRegex().Match(html).Value).Select(m => m.Groups[1].Value).ToList();

    private static List<string> ChipTexts(string html) =>
        ChipRegex().Matches(html).Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();

    private static List<string> CategoryNames(string html) =>
        CategoryNameRegex().Matches(html).Select(m => m.Groups[1].Value).ToList();

    private static List<string> ProductNames(string html) =>
        ProductNameRegex().Matches(html).Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();

    private static List<string> CartNames(string html) =>
        CartNameRegex().Matches(html).Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();

    private static List<string> Headings(string html) =>
        HeadingRegex().Matches(html).Select(m => m.Groups[1].Value).ToList();

    [GeneratedRegex("<select[^>]*data-category-select[\\s\\S]*?</select>")]
    private static partial Regex SelectRegex();

    [GeneratedRegex("<option[^>]*>([^<]+)</option>")]
    private static partial Regex OptionRegex();

    [GeneratedRegex("data-category-filter=\"[^\"]*\">([^<]+)</a>")]
    private static partial Regex ChipRegex();

    [GeneratedRegex("data-category-name>([^<]+)</span>")]
    private static partial Regex CategoryNameRegex();

    [GeneratedRegex("data-product-name>([^<]+)</span>")]
    private static partial Regex ProductNameRegex();

    [GeneratedRegex("data-cart-add data-product-id=\"\\d+\" data-name=\"([^\"]+)\"")]
    private static partial Regex CartNameRegex();

    [GeneratedRegex("data-storefront-category>([^<]+)</h2>")]
    private static partial Regex HeadingRegex();
}
