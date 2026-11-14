using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Local133.Application.Payments;
using Local133.Application.Sales;
using Local133.Domain.Merchants;
using Local133.Domain.Pey;
using Local133.Domain.Sales;
using Local133.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Local133.IntegrationTests.Sales;

/// <summary>Paso 3.2: venta con catálogo (carrito) cobrada con QR o al celular; <c>SaleItems</c> copia nombre y precio.</summary>
public class CartSaleTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    private const string Url = "/app/cobrar/productos";
    private const string CatalogUrl = "/app/cobrar/productos/catalogo";

    // --- Página y catálogo ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Vendedor_activo_ve_el_catalogo_y_el_carrito()
    {
        var client = await ActiveMerchantAsync(Email);
        var tacoId = await CreateProductAsync(Email, "Taco al pastor", 25m);

        var html = await GetHtmlAsync(client, Url);

        Assert.Contains("data-charge-mode-nav", html);
        Assert.Matches(new Regex("class=\"nav-link active\" href=\"/app/cobrar/productos\" aria-current=\"page\""), html);
        Assert.Matches(new Regex("class=\"nav-link active\" href=\"/app/cobrar\""), html); // pestaña "Cobrar"
        Assert.Contains($"data-product-id=\"{tacoId}\"", html);
        Assert.Contains("data-price=\"25.00\"", html);
        Assert.Contains("data-cart-form", html);
        Assert.Contains("data-charge-form", html);
        Assert.Contains("name=\"Input.Concept\"", html);
        Assert.Contains("name=\"Input.ExpectedTotal\"", html);
        Assert.Contains("name=\"Input.PhoneNumber\"", html);
        Assert.Contains("data-results-url=\"/app/cobrar/productos/catalogo\"", html);
        Assert.Contains("/js/cart", html);
        Assert.Contains("/js/product-search", html);
        Assert.Contains("/js/charge-form", html);
        Assert.Contains($"data-storage-key=\"local133:cart:{await MerchantIdAsync(Email)}\"", html);
        Assert.Contains("data-cart-initial>[]<", html);
    }

    [Fact]
    public async Task El_cobro_por_monto_lleva_al_carrito_con_el_selector()
    {
        var client = await ActiveMerchantAsync(Email);

        var html = await GetHtmlAsync(client, "/app/cobrar");

        Assert.Contains("data-charge-mode-nav", html);
        Assert.Contains("href=\"/app/cobrar/productos\"", html);
    }

    [Fact]
    public async Task Sin_productos_muestra_el_aviso_para_agregarlos()
    {
        var client = await ActiveMerchantAsync(Email);

        var html = await GetHtmlAsync(client, Url);

        Assert.Contains("data-cart-catalog-empty", html);
        Assert.Contains("href=\"/app/productos/nuevo\"", html);
        Assert.DoesNotContain("data-cart-form", html);
    }

    [Theory]
    [InlineData(MerchantStatus.PendingBankAccount, "Registra tu cuenta bancaria")]
    [InlineData(MerchantStatus.Suspended, "Tu cuenta está suspendida")]
    public async Task Vendedor_que_no_puede_cobrar_ve_el_aviso_y_su_POST_no_llama_a_Pey(MerchantStatus status, string notice)
    {
        var client = await ActiveMerchantAsync(Email, status: status);
        var tacoId = await CreateProductAsync(Email, "Taco al pastor", 25m);

        var html = await GetHtmlAsync(client, Url);
        Assert.Contains("data-charge-blocked", html);
        Assert.Contains(notice, html);
        Assert.DoesNotContain("data-cart-form", html);

        var response = await PostFormAsync(client, TokenPage, CartFields((tacoId, 1)), postUrl: Url);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Url, Location(response));
        AssertNoPeyCalls();
        Assert.Empty(await LoadSalesAsync());
    }

    [Fact]
    public async Task El_catalogo_busca_pagina_y_no_muestra_bajas_ni_productos_de_otro_vendedor()
    {
        var client = await ActiveMerchantAsync(Email);
        await ActiveMerchantAsync(OtherEmail);
        for (var i = 1; i <= 25; i++)
        {
            await CreateProductAsync(Email, $"Taco {i:D2}", 10m + i);
        }

        var deletedId = await CreateProductAsync(Email, "Taco borrado", 5m);
        await WithDbAsync(async db =>
        {
            (await db.Products.SingleAsync(p => p.Id == deletedId)).Delete(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        });
        var otherId = await CreateProductAsync(OtherEmail, "Taco ajeno", 5m);

        var first = await client.GetAsync($"{CatalogUrl}?q=Taco");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("no-store", first.Headers.CacheControl?.ToString());
        var html = WebUtility.HtmlDecode(await first.Content.ReadAsStringAsync());
        Assert.DoesNotContain("<html", html); // fragmento
        Assert.Contains("25 resultados", html);
        Assert.Equal(20, Regex.Count(html, "data-cart-add "));
        Assert.Contains("href=\"/app/cobrar/productos?q=Taco&pagina=2\"", html);
        Assert.Contains("href=\"/app/cobrar/productos\" data-clear-search", html);
        Assert.DoesNotContain($"data-product-id=\"{deletedId}\"", html);
        Assert.DoesNotContain($"data-product-id=\"{otherId}\"", html);

        var second = WebUtility.HtmlDecode(await GetHtmlAsync(client, $"{CatalogUrl}?q=Taco&pagina=2"));
        Assert.Equal(5, Regex.Count(second, "data-cart-add "));
    }

    // --- Cobro con QR -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Cobra_con_QR_y_guarda_la_copia_de_los_productos()
    {
        var client = await ActiveMerchantAsync(Email);
        var tacoId = await CreateProductAsync(Email, "Taco al pastor", 25m);
        var sodaId = await CreateProductAsync(Email, "Refresco", 20.5m);

        var response = await PostFormAsync(client, Url, CartFields((tacoId, 2), (sodaId, 1), expectedTotal: "70.50",
            concept: "2 Taco al pastor, Refresco"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var sale = Assert.Single(await LoadSalesAsync());
        Assert.Equal(SaleUrl(sale.Id), Location(response));
        Assert.Equal(SaleSource.Cart, sale.Source);
        Assert.Equal(ChargeMethod.DynamicQr, sale.Method);
        Assert.Equal(70.50m, sale.Amount);
        Assert.Equal("Tacos Don Ramón · 2 Taco al pastor, Refresco", sale.Concept);
        Assert.NotNull(sale.PeyQrId);

        var items = await LoadItemsAsync(sale.Id);
        Assert.Collection(items,
            taco =>
            {
                Assert.Equal(tacoId, taco.ProductId);
                Assert.Equal("Taco al pastor", taco.Name);
                Assert.Equal(25m, taco.UnitPrice);
                Assert.Equal(2, taco.Quantity);
                Assert.Equal(50m, taco.Amount);
                Assert.Equal(sale.MerchantId, taco.MerchantId);
            },
            soda =>
            {
                Assert.Equal(sodaId, soda.ProductId);
                Assert.Equal(20.5m, soda.Amount);
            });

        var sent = Assert.Single(Factory.Gateway.CodiQrRequests);
        Assert.Equal(70.50m, sent.Amount);
        Assert.Equal("Tacos Don Ramón · 2 Taco al pastor, Refresco", sent.Concept);
        Assert.Equal(sale.Id.ToString(CultureInfo.InvariantCulture), sent.Metadata![SaleMetadata.SaleIdKey]);
    }

    [Fact]
    public async Task El_vendedor_puede_cambiar_el_concepto()
    {
        var client = await ActiveMerchantAsync(Email);
        var tacoId = await CreateProductAsync(Email, "Taco al pastor", 25m);

        await PostFormAsync(client, Url, CartFields((tacoId, 1), concept: "Orden mesa 4"));

        var sale = Assert.Single(await LoadSalesAsync());
        Assert.Equal("Tacos Don Ramón · Orden mesa 4", sale.Concept);
    }

    [Fact]
    public async Task La_venta_muestra_sus_productos_y_nuevo_cobro_vuelve_al_catalogo()
    {
        var client = await ActiveMerchantAsync(Email);
        var tacoId = await CreateProductAsync(Email, "Taco al pastor", 25m);
        var sodaId = await CreateProductAsync(Email, "Refresco", 20m);
        await PostFormAsync(client, Url, CartFields((tacoId, 2), (sodaId, 1), expectedTotal: "70.00"));
        var sale = Assert.Single(await LoadSalesAsync());

        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, SaleUrl(sale.Id)));

        Assert.Contains("data-sale-items", html);
        Assert.Contains("Ver 3 productos", html);
        Assert.Equal(2, Regex.Count(html, "data-sale-item>"));
        Assert.Contains("2 × $25.00", html);
        Assert.Contains("$50.00", html);
        Assert.Contains("href=\"/app/cobrar/productos\" data-new-charge", html);
        Assert.Contains("data-sale-qr", html);
    }

    [Fact]
    public async Task Editar_o_borrar_el_producto_no_cambia_la_venta()
    {
        var client = await ActiveMerchantAsync(Email);
        var tacoId = await CreateProductAsync(Email, "Taco al pastor", 25m);
        await PostFormAsync(client, Url, CartFields((tacoId, 2), expectedTotal: "50.00"));
        var sale = Assert.Single(await LoadSalesAsync());

        await WithDbAsync(async db =>
        {
            var product = await db.Products.SingleAsync(p => p.Id == tacoId);
            product.Update("Taco campechano", null, 30m, DateTimeOffset.UtcNow);
            product.Delete(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        });

        var item = Assert.Single(await LoadItemsAsync(sale.Id));
        Assert.Equal("Taco al pastor", item.Name);
        Assert.Equal(25m, item.UnitPrice);
        Assert.Equal(50m, (await LoadSaleAsync(sale.Id)).Amount);
        Assert.Contains("Taco al pastor", WebUtility.HtmlDecode(await GetHtmlAsync(client, SaleUrl(sale.Id))));
    }

    [Fact]
    public async Task El_webhook_marca_pagada_la_venta_con_carrito()
    {
        var client = await ActiveMerchantAsync(Email);
        var tacoId = await CreateProductAsync(Email, "Taco al pastor", 25m);
        await PostFormAsync(client, Url, CartFields((tacoId, 2), expectedTotal: "50.00"));
        var sale = Assert.Single(await LoadSalesAsync());

        var response = await PostWebhookAsync(QrPaymentBodyFor(sale));

        Assert.Equal("Processed", await WebhookStatusAsync(response));
        Assert.Equal(SaleStatus.Paid, (await LoadSaleAsync(sale.Id)).Status);
    }

    // --- Cobro al celular -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Cobra_al_celular_con_los_productos()
    {
        var client = await ActiveMerchantAsync(Email);
        var tacoId = await CreateProductAsync(Email, "Taco al pastor", 25m);

        var fields = CartFields((tacoId, 3), expectedTotal: "75.00");
        fields["Input.Method"] = nameof(ChargeMethod.PushNotification);
        fields["Input.PhoneNumber"] = "+52 (55) 1234-5678";
        var response = await PostFormAsync(client, Url, fields);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var sale = Assert.Single(await LoadSalesAsync());
        Assert.Equal(SaleSource.Cart, sale.Source);
        Assert.Equal(ChargeMethod.PushNotification, sale.Method);
        Assert.Equal("5512345678", sale.PhoneNumber);
        Assert.Equal(75m, sale.Amount);
        Assert.Single(await LoadItemsAsync(sale.Id));
        var sent = Assert.Single(Factory.Gateway.PushRequests);
        Assert.Equal(75m, sent.Amount);
        Assert.Empty(Factory.Gateway.CodiQrRequests);
    }

    [Fact]
    public async Task Celular_obligatorio_con_push_sin_llamar_a_Pey()
    {
        var client = await ActiveMerchantAsync(Email);
        var tacoId = await CreateProductAsync(Email, "Taco al pastor", 25m);

        var fields = CartFields((tacoId, 1));
        fields["Input.Method"] = nameof(ChargeMethod.PushNotification);
        var response = await PostFormAsync(client, Url, fields);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Escribe el celular de tu cliente.", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        AssertNoPeyCalls();
        Assert.Empty(await LoadSalesAsync());
    }

    // --- Errores sin llamar a Pey -----------------------------------------------------------------------------------

    [Fact]
    public async Task Producto_de_otro_vendedor_no_se_cobra()
    {
        var client = await ActiveMerchantAsync(Email);
        await ActiveMerchantAsync(OtherEmail);
        await CreateProductAsync(Email, "Taco al pastor", 25m); // con catálogo propio el formulario vuelve con el error
        var otherId = await CreateProductAsync(OtherEmail, "Taco ajeno", 25m);

        var html = await PostWithErrorAsync(client, CartFields((otherId, 1)));

        Assert.Contains("Uno de los productos ya no está en tu catálogo", html);
        Assert.Contains("data-cart-initial>[]<", html); // el producto ajeno no se devuelve
    }

    [Fact]
    public async Task Producto_dado_de_baja_no_se_cobra_y_el_carrito_se_rearma_sin_el()
    {
        var client = await ActiveMerchantAsync(Email);
        var tacoId = await CreateProductAsync(Email, "Taco al pastor", 25m);
        var sodaId = await CreateProductAsync(Email, "Refresco", 20m);
        await WithDbAsync(async db =>
        {
            (await db.Products.SingleAsync(p => p.Id == sodaId)).Delete(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        });

        var html = await PostWithErrorAsync(client, CartFields((tacoId, 2), (sodaId, 1), expectedTotal: "70.00"));

        Assert.Contains("Uno de los productos ya no está en tu catálogo", html);
        Assert.Contains($"\"productId\":{tacoId}", html);
        Assert.Contains("\"quantity\":2", html);
        Assert.DoesNotContain($"\"productId\":{sodaId}", html);
        Assert.Contains("data-has-errors=\"true\"", html);
    }

    [Fact]
    public async Task Si_cambio_el_precio_no_se_cobra_y_el_carrito_trae_el_precio_nuevo()
    {
        var client = await ActiveMerchantAsync(Email);
        var tacoId = await CreateProductAsync(Email, "Taco al pastor", 25m);
        await WithDbAsync(async db =>
        {
            (await db.Products.SingleAsync(p => p.Id == tacoId)).Update("Taco al pastor", null, 28m, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        });

        var html = await PostWithErrorAsync(client, CartFields((tacoId, 2), expectedTotal: "50.00", concept: "Mi concepto"));

        Assert.Contains("Cambió el precio de algún producto", html);
        Assert.Contains("\"price\":\"28.00\"", html);
        Assert.Contains("value=\"Mi concepto\"", html); // se conserva lo que escribió
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1000")]
    [InlineData("abc")]
    public async Task Cantidad_invalida_no_se_cobra(string quantity)
    {
        var client = await ActiveMerchantAsync(Email);
        var tacoId = await CreateProductAsync(Email, "Taco al pastor", 25m);
        var fields = CartFields((tacoId, 1));
        fields["Input.Lines[0].Quantity"] = quantity;

        var html = await PostWithErrorAsync(client, fields);

        Assert.Contains("La cantidad de cada producto debe ser de 1 a 999.", html);
    }

    [Fact]
    public async Task Sin_productos_no_se_cobra()
    {
        var client = await ActiveMerchantAsync(Email);
        await CreateProductAsync(Email, "Taco al pastor", 25m);

        var html = await PostWithErrorAsync(client, new Dictionary<string, string>
        {
            ["Input.Concept"] = "Algo",
            ["Input.ExpectedTotal"] = "10.00",
        });

        Assert.Contains("Agrega al menos un producto a la venta.", html);
    }

    [Fact]
    public async Task Producto_repetido_no_se_cobra()
    {
        var client = await ActiveMerchantAsync(Email);
        var tacoId = await CreateProductAsync(Email, "Taco al pastor", 25m);

        var html = await PostWithErrorAsync(client, CartFields((tacoId, 1), (tacoId, 1), expectedTotal: "50.00"));

        Assert.Contains("Un producto aparece dos veces", html);
    }

    [Fact]
    public async Task Mas_de_50_productos_distintos_no_se_cobra()
    {
        var client = await ActiveMerchantAsync(Email);
        var lines = new List<(int, int)>();
        for (var i = 1; i <= 51; i++)
        {
            lines.Add((await CreateProductAsync(Email, $"Producto {i}", 1m), 1));
        }

        var html = await PostWithErrorAsync(client, CartFields([.. lines], expectedTotal: "51.00"));

        Assert.Contains("hasta 50 productos distintos", html);
    }

    [Fact]
    public async Task Total_mayor_al_maximo_no_se_cobra()
    {
        var client = await ActiveMerchantAsync(Email);
        var id = await CreateProductAsync(Email, "Joya", 999_999.99m);

        var html = await PostWithErrorAsync(client, CartFields((id, 2), expectedTotal: "999999.99"));

        Assert.Contains("El total máximo de una venta es $999,999.99.", html);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12,50")]
    [InlineData("-5")]
    public async Task Total_esperado_ilegible_no_se_cobra(string expectedTotal)
    {
        var client = await ActiveMerchantAsync(Email);
        var tacoId = await CreateProductAsync(Email, "Taco al pastor", 25m);

        var html = await PostWithErrorAsync(client, CartFields((tacoId, 1), expectedTotal: expectedTotal));

        Assert.Contains("El total de la venta debe estar entre", html);
    }

    [Theory]
    [InlineData("", "Escribe el concepto del cobro.")]
    [InlineData("Tacos 🌮", "Usa solo letras")]
    public async Task Concepto_invalido_no_se_cobra(string concept, string message)
    {
        var client = await ActiveMerchantAsync(Email);
        var tacoId = await CreateProductAsync(Email, "Taco al pastor", 25m);

        var html = await PostWithErrorAsync(client, CartFields((tacoId, 1), concept: concept));

        Assert.Contains(message, html);
    }

    // --- Fallas de Pey ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Si_Pey_rechaza_el_QR_se_borran_la_venta_y_sus_productos()
    {
        Factory.Gateway.CodiQrException = new PaymentGatewayValidationException(
            "monto inválido", PeyErrorType.InvalidAmount, 400, "req-1", "Invalid amount");
        var client = await ActiveMerchantAsync(Email);
        var tacoId = await CreateProductAsync(Email, "Taco al pastor", 25m);

        var response = await PostFormAsync(client, Url, CartFields((tacoId, 2), expectedTotal: "50.00"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("El servicio de pagos no aceptó el cobro", html);
        Assert.Contains($"\"productId\":{tacoId}", html); // el carrito se conserva
        Assert.Empty(await LoadSalesAsync());
        Assert.Equal(0, await WithDbAsync(db => db.SaleItems.IgnoreQueryFilters().CountAsync()));
    }

    [Fact]
    public async Task Push_sin_respuesta_de_Pey_conserva_la_venta_cancelada_con_sus_productos()
    {
        Factory.Gateway.PushException = new PaymentGatewayUnavailableException("timeout");
        var client = await ActiveMerchantAsync(Email);
        var tacoId = await CreateProductAsync(Email, "Taco al pastor", 25m);
        var fields = CartFields((tacoId, 2), expectedTotal: "50.00");
        fields["Input.Method"] = nameof(ChargeMethod.PushNotification);
        fields["Input.PhoneNumber"] = "5512345678";

        var response = await PostFormAsync(client, Url, fields);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var sale = Assert.Single(await LoadSalesAsync());
        Assert.Equal(SaleStatus.Cancelled, sale.Status);
        Assert.Single(await LoadItemsAsync(sale.Id));
    }

    // --- Seguridad --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task POST_sin_antiforgery_se_rechaza()
    {
        var client = await ActiveMerchantAsync(Email);
        var tacoId = await CreateProductAsync(Email, "Taco al pastor", 25m);

        var response = await client.PostAsync(Url, new FormUrlEncodedContent(CartFields((tacoId, 1))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoPeyCalls();
    }

    [Fact]
    public async Task Otro_vendedor_no_ve_la_venta_ni_sus_productos()
    {
        var client = await ActiveMerchantAsync(Email);
        var other = await ActiveMerchantAsync(OtherEmail);
        var tacoId = await CreateProductAsync(Email, "Taco al pastor", 25m);
        await PostFormAsync(client, Url, CartFields((tacoId, 1)));
        var sale = Assert.Single(await LoadSalesAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(SaleUrl(sale.Id))).StatusCode);
    }

    [Fact]
    public async Task Sin_sesion_redirige_al_login()
    {
        var response = await CreateClient().GetAsync(Url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Identity/Account/Login", Location(response));
    }

    // --- Ayudas -----------------------------------------------------------------------------------------------------

    private static Dictionary<string, string> CartFields(
        (int ProductId, int Quantity) line, string expectedTotal = "25.00", string concept = "Taco al pastor") =>
        CartFields([line], expectedTotal, concept);

    private static Dictionary<string, string> CartFields(
        (int ProductId, int Quantity) first, (int ProductId, int Quantity) second, string expectedTotal,
        string concept = "Taco al pastor, Refresco") =>
        CartFields([first, second], expectedTotal, concept);

    private static Dictionary<string, string> CartFields(
        (int ProductId, int Quantity)[] lines, string expectedTotal, string concept = "Productos")
    {
        var fields = new Dictionary<string, string>
        {
            ["Input.Concept"] = concept,
            ["Input.ExpectedTotal"] = expectedTotal,
        };
        for (var i = 0; i < lines.Length; i++)
        {
            fields[$"Input.Lines[{i}].ProductId"] = lines[i].ProductId.ToString(CultureInfo.InvariantCulture);
            fields[$"Input.Lines[{i}].Quantity"] = lines[i].Quantity.ToString(CultureInfo.InvariantCulture);
        }

        return fields;
    }

    /// <summary>POST que debe volver al formulario (200) sin llamar a Pey ni guardar la venta; devuelve el HTML.</summary>
    private async Task<string> PostWithErrorAsync(HttpClient client, Dictionary<string, string> fields)
    {
        var response = await PostFormAsync(client, Url, fields);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoPeyCalls();
        Assert.Empty(await LoadSalesAsync());
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private void AssertNoPeyCalls()
    {
        Assert.Empty(Factory.Gateway.CodiQrRequests);
        Assert.Empty(Factory.Gateway.PushRequests);
    }

    private Task<List<Sale>> LoadSalesAsync() =>
        WithDbAsync(db => db.Sales.IgnoreQueryFilters().AsNoTracking().OrderBy(s => s.Id).ToListAsync());

    private Task<List<SaleItem>> LoadItemsAsync(int saleId) =>
        WithDbAsync(db => db.SaleItems.IgnoreQueryFilters().AsNoTracking().Where(i => i.SaleId == saleId).OrderBy(i => i.Id).ToListAsync());
}
