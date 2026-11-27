using System.Globalization;
using System.Net;
using Local133.Domain.Common;
using Local133.Domain.Sales;
using Local133.Domain.Settings;
using Local133.IntegrationTests.Infrastructure;
using Local133.IntegrationTests.Sales;
using Local133.Web.Areas.Merchant.Models;
using Microsoft.EntityFrameworkCore;

namespace Local133.IntegrationTests.Subscriptions;

/// <summary>
/// 5.14: lo que permite cada plan, aplicado en el servidor. El vencido no genera cobros (monto, celular ni carrito) y
/// sin llamar a Pey, pero lee su historial y comparte sus recibos; Básico cobra por monto y celular, con historial
/// completo, búsqueda, corte y WhatsApp, sin carrito, edición del catálogo ni Excel. El uso justo avisa en los dos planes.
/// </summary>
public class PlanAccessTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    private const string CartUrl = "/app/cobrar/productos";

    // --- Vencido: no genera cobros --------------------------------------------------------------------------------------

    [Fact]
    public async Task El_vencido_no_genera_QR_ni_push_ni_llama_a_Pey()
    {
        var client = await ExpiredMerchantAsync(Email);
        var before = await SalesCountAsync();

        var qr = await PostFormAsync(client, TokenPage, AmountForm(), postUrl: "/app/cobrar");
        var push = await PostFormAsync(client, TokenPage, new Dictionary<string, string>(AmountForm())
        {
            ["Input.Method"] = nameof(ChargeMethod.PushNotification),
            ["Input.PhoneNumber"] = "55 1234 5678",
        }, postUrl: "/app/cobrar");

        foreach (var response in new[] { qr, push })
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/app/cobrar", Location(response));
        }

        Assert.Equal(before, await SalesCountAsync());
        Assert.Empty(Factory.Gateway.CodiQrRequests);
        Assert.Empty(Factory.Gateway.PushRequests);
    }

    [Fact]
    public async Task Cobrar_del_vencido_muestra_el_aviso_en_lugar_del_formulario()
    {
        var client = await ExpiredMerchantAsync(Email);
        await CreateProductAsync(Email, "Taco al pastor", 25m);

        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, "/app/cobrar"));

        Assert.Contains("data-plan-expired", html);
        Assert.Contains("Tu plan venció el", html);
        Assert.Contains(PlanExpiredViewModel.DefaultMessage, html);
        Assert.Contains($"href=\"{PlanUrls.ChoosePlan}\"", html);
        Assert.DoesNotContain("data-charge-form", html);
        Assert.DoesNotContain("data-charge-missing-catalog", html);
        Assert.DoesNotContain("/js/charge-form.js", html);
        // En Cobrar ya está el aviso completo: el banner no se repite.
        Assert.DoesNotContain("data-plan-banner", html);
    }

    [Fact]
    public async Task El_vencido_no_cobra_con_carrito_y_el_catalogo_responde_403()
    {
        var client = await ExpiredMerchantAsync(Email);
        var tacoId = await CreateProductAsync(Email, "Taco al pastor", 25m);

        var page = WebUtility.HtmlDecode(await GetHtmlAsync(client, CartUrl));
        var catalog = await client.GetAsync($"{CartUrl}/catalogo");
        var response = await PostFormAsync(client, TokenPage, CartForm(tacoId), postUrl: CartUrl);

        Assert.Contains("data-plan-expired", page);
        Assert.DoesNotContain("data-cart-pro-required", page);
        Assert.DoesNotContain("/js/cart", page);
        Assert.Equal(HttpStatusCode.Forbidden, catalog.StatusCode);
        Assert.Equal(CartUrl, Location(response));
        Assert.Equal(0, await SalesCountAsync());
        Assert.Empty(Factory.Gateway.CodiQrRequests);
    }

    [Fact]
    public async Task Una_venta_generada_antes_de_vencer_se_abona_despues()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedTrialAsync(Email);
        var sale = await ChargeAsync(client);
        await SeedExpiredPlanAsync(Email);

        var response = await PostWebhookAsync(QrPaymentBodyFor(sale));

        Assert.Equal("Processed", await WebhookStatusAsync(response));
        Assert.Equal(SaleStatus.Paid, (await LoadSaleAsync(sale.Id)).Status);
    }

    [Fact]
    public async Task Una_venta_con_carrito_de_antes_de_vencer_se_abona_despues()
    {
        await ExpiredMerchantAsync(Email);
        var tacoId = await CreateProductAsync(Email, "Taco al pastor", 25m);
        var sale = await SeedCartSaleAsync(Email, tacoId);

        var response = await PostWebhookAsync(QrPaymentBodyFor(sale));

        Assert.Equal("Processed", await WebhookStatusAsync(response));
        Assert.Equal(SaleStatus.Paid, (await LoadSaleAsync(sale.Id)).Status);
    }

    // --- Vencido: lo que sí puede hacer ---------------------------------------------------------------------------------

    [Fact]
    public async Task El_vencido_ve_todo_su_historial_con_busqueda_y_comparte_por_WhatsApp()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedTrialAsync(Email);
        var sale = await ChargeByPhoneAsync(client);
        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(PushPaymentBodyFor(sale))));
        var now = DateTimeOffset.UtcNow;
        var old = (await SeedSalesAsync(Email, 1, paidAt: now.AddDays(-60), createdAt: now.AddDays(-60), concept: "Vieja"))[0];
        await SeedExpiredPlanAsync(Email);

        var list = WebUtility.HtmlDecode(await GetHtmlAsync(client, "/app/ventas"));
        var search = await GetHtmlAsync(client, "/app/ventas?q=Vieja");
        var oldDetail = await client.GetAsync($"/app/ventas/{old}");
        var detail = WebUtility.HtmlDecode(await GetHtmlAsync(client, $"/app/ventas/{sale.Id}"));
        var whatsApp = await client.GetAsync($"/app/ventas/{sale.Id}/whatsapp");

        Assert.Contains($"data-sale-row=\"{old}\"", list);
        Assert.Contains($"data-sale-row=\"{sale.Id}\"", list);
        Assert.Contains("data-sales-search", list);
        Assert.Contains("data-date-filter", list);
        Assert.DoesNotContain("data-sales-export", list);
        Assert.Contains("data-plan-banner=\"expired\"", list);
        Assert.Contains($"data-sale-row=\"{old}\"", search);
        Assert.DoesNotContain($"data-sale-row=\"{sale.Id}\"", search);
        Assert.Equal(HttpStatusCode.OK, oldDetail.StatusCode);
        Assert.Contains("data-receipt-whatsapp", detail);
        Assert.Contains("data-receipt-native-share", detail);
        Assert.Equal(HttpStatusCode.Redirect, whatsApp.StatusCode);
        Assert.Equal("wa.me", whatsApp.Headers.Location!.Host);
        Assert.StartsWith("/525512345678", Location(whatsApp));
    }

    [Fact]
    public async Task El_vencido_no_ve_el_corte_ni_exporta_y_su_catalogo_es_de_solo_lectura()
    {
        var client = await ExpiredMerchantAsync(Email);
        await CreateProductAsync(Email, "Taco al pastor", 25m);

        var cashCut = await client.GetAsync("/app/ventas/corte");
        var cashCutHtml = WebUtility.HtmlDecode(await cashCut.Content.ReadAsStringAsync());
        var export = await client.GetAsync("/app/ventas/exportar");
        var products = await GetHtmlAsync(client, "/app/productos");
        var newProduct = await client.GetAsync("/app/productos/nuevo");

        Assert.Equal(HttpStatusCode.OK, cashCut.StatusCode);
        Assert.Contains("data-cash-cut-locked", cashCutHtml);
        Assert.Contains("data-plan-expired", cashCutHtml);
        Assert.Contains("volver a ver tu corte de caja", cashCutHtml);
        Assert.Equal(HttpStatusCode.Forbidden, export.StatusCode);
        Assert.Contains("data-products-readonly-plan=\"expired\"", products);
        Assert.Contains("data-plan-expired-choose", products);
        Assert.Equal(HttpStatusCode.Redirect, newProduct.StatusCode);
        Assert.Equal("/app/productos", Location(newProduct));
    }

    [Fact]
    public async Task Inicio_del_vencido_muestra_la_tarjeta_Tu_plan_vencio_sin_repetir_el_plan()
    {
        var client = await ExpiredMerchantAsync(Email);

        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, "/app"));

        Assert.Contains("data-home-plan-expired", html);
        Assert.Contains("Tu plan venció el", html);
        Assert.Contains("data-plan-expired-choose", html);
        Assert.DoesNotContain("Ya puedes cobrar", html);
        Assert.DoesNotContain("data-plan-tier", html);
        Assert.DoesNotContain("data-plan-banner", html);
        Assert.DoesNotContain("data-charge-usage", html);
    }

    // --- Básico ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Basico_cobra_por_monto_y_por_celular()
    {
        var client = await BasicMerchantAsync(Email);
        await CreateProductAsync(Email, "Taco al pastor", 25m);

        var form = WebUtility.HtmlDecode(await GetHtmlAsync(client, "/app/cobrar"));
        var qr = await ChargeAsync(client);
        var push = await ChargeByPhoneAsync(client);

        Assert.Contains("data-charge-form", form);
        Assert.DoesNotContain("data-plan-expired", form);
        Assert.Contains("data-charge-missing-catalog", form);
        Assert.Contains("Tu catálogo de 1 producto te espera en Pro.", form);
        Assert.Equal(SaleStatus.Pending, qr.Status);
        Assert.Equal(SaleStatus.Pending, push.Status);
        Assert.Single(Factory.Gateway.CodiQrRequests);
        Assert.Single(Factory.Gateway.PushRequests);
    }

    [Fact]
    public async Task Basico_ve_el_historial_completo_con_busqueda_fechas_y_corte()
    {
        var client = await BasicMerchantAsync(Email);
        var now = DateTimeOffset.UtcNow;
        var old = (await SeedSalesAsync(Email, 1, paidAt: null, createdAt: now.AddDays(-60), concept: "Vieja"))[0];
        var recent = (await SeedSalesAsync(Email, 1, paidAt: null, createdAt: now.AddDays(-1), concept: "Reciente"))[0];
        var today = MexicoTime.Today(now);

        var list = await GetHtmlAsync(client, "/app/ventas");
        var search = await GetHtmlAsync(client, "/app/ventas?q=Vieja");
        var dates = await GetHtmlAsync(client,
            $"/app/ventas?desde={SaleFormats.IsoDate(today.AddDays(-70))}&hasta={SaleFormats.IsoDate(today.AddDays(-50))}");
        var cashCut = await GetHtmlAsync(client, "/app/ventas/corte");
        var export = await client.GetAsync("/app/ventas/exportar");

        Assert.Contains($"data-sale-row=\"{old}\"", list);
        Assert.Contains($"data-sale-row=\"{recent}\"", list);
        Assert.DoesNotContain("data-cash-cut-locked-icon", list);
        Assert.DoesNotContain("data-sales-export", list);
        Assert.Contains($"data-sale-row=\"{old}\"", search);
        Assert.DoesNotContain($"data-sale-row=\"{recent}\"", search);
        Assert.Contains($"data-sale-row=\"{old}\"", dates);
        Assert.DoesNotContain($"data-sale-row=\"{recent}\"", dates);
        Assert.DoesNotContain("data-cash-cut-locked", cashCut);
        Assert.Equal(HttpStatusCode.Forbidden, export.StatusCode);
    }

    [Fact]
    public async Task Basico_comparte_el_recibo_por_WhatsApp()
    {
        var client = await BasicMerchantAsync(Email);
        var sale = await ChargeByPhoneAsync(client);
        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(PushPaymentBodyFor(sale))));

        var detail = await GetHtmlAsync(client, $"/app/ventas/{sale.Id}");
        var whatsApp = await client.GetAsync($"/app/ventas/{sale.Id}/whatsapp");

        Assert.Contains("data-receipt-whatsapp-customer", detail);
        Assert.DoesNotContain("data-receipt-share-limited", detail);
        Assert.Equal("wa.me", whatsApp.Headers.Location!.Host);
        Assert.StartsWith("/525512345678", Location(whatsApp));
    }

    [Fact]
    public async Task Basico_no_cobra_con_carrito_ni_llama_a_Pey()
    {
        var client = await BasicMerchantAsync(Email);
        var tacoId = await CreateProductAsync(Email, "Taco al pastor", 25m);
        await CreateProductAsync(Email, "Refresco", 20m);

        var page = WebUtility.HtmlDecode(await GetHtmlAsync(client, CartUrl));
        var catalog = await client.GetAsync($"{CartUrl}/catalogo");
        var response = await PostFormAsync(client, TokenPage, CartForm(tacoId), postUrl: CartUrl);

        Assert.Contains("data-cart-pro-required", page);
        Assert.Contains("Con Básico puedes cobrar por monto", page);
        Assert.Contains("Tu catálogo de 2 productos te espera en Pro.", page);
        Assert.DoesNotContain("/js/cart", page);
        Assert.Equal(HttpStatusCode.Forbidden, catalog.StatusCode);
        Assert.Equal(CartUrl, Location(response));
        Assert.Equal(0, await SalesCountAsync());
        Assert.Empty(Factory.Gateway.CodiQrRequests);
    }

    [Fact]
    public async Task Basico_ve_su_catalogo_en_solo_lectura_sin_banner()
    {
        var client = await BasicMerchantAsync(Email);
        await CreateProductAsync(Email, "Taco al pastor", 25m);

        var products = WebUtility.HtmlDecode(await GetHtmlAsync(client, "/app/productos"));
        var newProduct = await client.GetAsync("/app/productos/nuevo");

        Assert.Contains("data-products-readonly-plan=\"basic\"", products);
        Assert.Contains("Con Básico consultas tus productos", products);
        Assert.DoesNotContain("data-new-product", products);
        Assert.DoesNotContain("data-plan-banner", products);
        Assert.Equal("/app/productos", Location(newProduct));
    }

    // --- Uso justo ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task El_uso_justo_avisa_una_vez_al_cruzarlo_en_Basico()
    {
        var client = await BasicMerchantAsync(Email);
        await SeedSalesAsync(Email, 2, paidAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var merchantId = await MerchantIdAsync(Email);

        await WithFairUseAsync(3, async () =>
        {
            var crossing = await ChargeAsync(client);
            await PostWebhookAsync(QrPaymentBodyFor(crossing));
            await PostWebhookAsync(QrPaymentBodyFor(crossing)); // pago adicional de la misma venta: no vuelve a avisar
            var after = await ChargeAsync(client);
            await PostWebhookAsync(QrPaymentBodyFor(after));
        });

        Assert.Equal(new[] { (merchantId, 3) }, Factory.LimitNotifier.FairUse.ToArray());
    }

    [Fact]
    public async Task El_uso_justo_avisa_en_la_prueba_y_no_antes()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedTrialAsync(Email);
        await SeedSalesAsync(Email, 1, paidAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        await WithFairUseAsync(3, async () =>
        {
            var second = await ChargeAsync(client);
            await PostWebhookAsync(QrPaymentBodyFor(second));
            Assert.Empty(Factory.LimitNotifier.FairUse);

            var third = await ChargeAsync(client);
            await PostWebhookAsync(QrPaymentBodyFor(third));
        });

        Assert.Single(Factory.LimitNotifier.FairUse);
    }

    [Fact]
    public async Task Si_el_aviso_del_uso_justo_falla_el_pago_queda_guardado()
    {
        var client = await BasicMerchantAsync(Email);
        Factory.LimitNotifier.FailWith = new InvalidOperationException("Aviso caído");

        Sale? sale = null;
        HttpResponseMessage? response = null;
        await WithFairUseAsync(1, async () =>
        {
            sale = await ChargeAsync(client);
            response = await PostWebhookAsync(QrPaymentBodyFor(sale));
        });

        Assert.Equal("Processed", await WebhookStatusAsync(response!));
        Assert.Equal(SaleStatus.Paid, (await LoadSaleAsync(sale!.Id)).Status);
        Assert.Equal(1, await WithDbAsync(db => db.Payments.IgnoreQueryFilters().CountAsync(p => p.SaleId == sale.Id)));
    }

    // --- Aislamiento y acceso -------------------------------------------------------------------------------------------

    [Fact]
    public async Task El_plan_de_un_vendedor_no_afecta_al_otro()
    {
        var expired = await ExpiredMerchantAsync(OtherEmail);
        var otherSale = (await SeedSalesAsync(OtherEmail, 1, paidAt: null))[0];
        var basic = await BasicMerchantAsync(Email);

        var sale = await ChargeAsync(basic);
        var blocked = await PostFormAsync(expired, TokenPage, AmountForm(), postUrl: "/app/cobrar");
        var foreign = await basic.GetAsync($"/app/ventas/{otherSale}");
        var foreignWhatsApp = await basic.GetAsync($"/app/ventas/{otherSale}/whatsapp");

        Assert.Equal(SaleStatus.Pending, sale.Status);
        Assert.Equal(sale.MerchantId, await MerchantIdAsync(Email));
        Assert.Equal("/app/cobrar", Location(blocked));
        Assert.Single(Factory.Gateway.CodiQrRequests);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignWhatsApp.StatusCode);
    }

    [Fact]
    public async Task Mi_plan_muestra_la_tabla_de_Basico_y_Pro()
    {
        var client = await BasicMerchantAsync(Email);

        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, "/app/plan"));

        Assert.Contains("data-plan-tier=\"Basic\"", html);
        Assert.Contains("data-plan-features", html);
        Assert.Contains(">Básico</th>", html);
        Assert.Contains("Ilimitados, con uso justo", html);
        Assert.Contains("Sí, con «Cobrado con Local133»", html);
        Assert.Contains("data-coming-soon", html);
        Assert.DoesNotContain("data-charge-usage", html);
        Assert.DoesNotContain("Gratuito", html);
    }

    [Fact]
    public async Task Mi_plan_es_solo_para_vendedores()
    {
        await CreateConfirmedUserAsync("admin@prueba.mx", Application.Auth.Roles.Admin);
        var admin = CreateClient();
        await LoginAsync(admin, "admin@prueba.mx");

        var asAdmin = await admin.GetAsync("/app/plan");
        var anonymous = await CreateClient().GetAsync("/app/plan");

        Assert.StartsWith("/Identity/Account/AccessDenied", Location(asAdmin));
        Assert.StartsWith("/Identity/Account/Login", Location(anonymous));
    }

    // --- Ayudas ---------------------------------------------------------------------------------------------------------

    private async Task<HttpClient> ExpiredMerchantAsync(string email)
    {
        var client = await ActiveMerchantAsync(email);
        await SeedExpiredPlanAsync(email);
        return client;
    }

    private async Task<HttpClient> BasicMerchantAsync(string email)
    {
        var client = await ActiveMerchantAsync(email);
        await SeedBasicPlanAsync(email);
        return client;
    }

    private static Dictionary<string, string> AmountForm() => new()
    {
        ["Input.Amount"] = "150.00",
        ["Input.Concept"] = "2 tacos al pastor",
    };

    private static Dictionary<string, string> CartForm(int productId) => new()
    {
        ["Input.Concept"] = "Taco al pastor",
        ["Input.ExpectedTotal"] = "25.00",
        ["Input.Lines[0].ProductId"] = productId.ToString(CultureInfo.InvariantCulture),
        ["Input.Lines[0].Quantity"] = "1",
    };

    /// <summary>Baja el uso justo mientras corre <paramref name="action"/> y lo restablece (Respawn no limpia los ajustes).</summary>
    private async Task WithFairUseAsync(int fairUse, Func<Task> action)
    {
        var original = await WithDbAsync(db =>
            db.SystemSettings.AsNoTracking().Where(s => s.Id == SystemSettings.SingletonId).Select(s => s.FairUseMonthlyPaid).SingleAsync());
        await SetFairUseAsync(fairUse);
        try
        {
            await action();
        }
        finally
        {
            await SetFairUseAsync(original);
        }
    }

    private Task SetFairUseAsync(int value) => WithDbAsync(db =>
        db.SystemSettings.Where(s => s.Id == SystemSettings.SingletonId)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.FairUseMonthlyPaid, value)));

    /// <summary>
    /// Ventas del vendedor insertadas directo en la base, creadas en <paramref name="createdAt"/> (o en
    /// <paramref name="paidAt"/>, o ahora) y pagadas en <paramref name="paidAt"/> si viene. Devuelve sus ids.
    /// </summary>
    private Task<List<int>> SeedSalesAsync(
        string email, int count, DateTimeOffset? paidAt, DateTimeOffset? createdAt = null, string concept = "Venta")
    {
        return WithDbAsync(async db =>
        {
            var merchantId = (await MerchantOfAsync(db, email)).Id;
            var folio = await db.Sales.IgnoreQueryFilters().Where(s => s.MerchantId == merchantId)
                .MaxAsync(s => (int?)s.Folio) ?? 0;
            var created = createdAt ?? paidAt ?? DateTimeOffset.UtcNow;
            var sales = Enumerable.Range(1, count).Select(i =>
            {
                var sale = Sale.StartDirect(merchantId, folio + i, 10m, concept, created.AddMinutes(10), created);
                if (paidAt is { } at)
                {
                    sale.MarkPaid(at);
                }

                return sale;
            }).ToList();
            db.Sales.AddRange(sales);
            await db.SaveChangesAsync();
            return sales.Select(s => s.Id).ToList();
        });
    }

    /// <summary>Venta con carrito pendiente, con su QR, como si se hubiera creado antes de vencer.</summary>
    private Task<Sale> SeedCartSaleAsync(string email, int productId) => WithDbAsync(async db =>
    {
        var merchantId = (await MerchantOfAsync(db, email)).Id;
        var product = await db.Products.IgnoreQueryFilters().SingleAsync(p => p.Id == productId);
        var now = DateTimeOffset.UtcNow;
        var sale = Sale.StartDirect(merchantId, 1, product.Price, "Taco al pastor", now.AddMinutes(10), now,
            [SaleItem.FromProduct(product, 1)]);
        sale.AttachQr("IDC-CARRITO-1", FakePaymentGateway.QrStringFor("IDC-CARRITO-1"));
        db.Sales.Add(sale);
        await db.SaveChangesAsync();
        return sale;
    });

    private Task<int> SalesCountAsync() => WithDbAsync(db => db.Sales.IgnoreQueryFilters().CountAsync());
}
