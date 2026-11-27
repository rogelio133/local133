using System.Globalization;
using System.Net;
using Local133.Domain.Common;
using Local133.Domain.Sales;
using Local133.Domain.Subscriptions;
using Local133.IntegrationTests.Infrastructure;
using Local133.IntegrationTests.Sales;
using Local133.Web.Areas.Merchant.Models;
using Microsoft.EntityFrameworkCore;

namespace Local133.IntegrationTests.Subscriptions;

/// <summary>
/// 5.5: límites del gratuito aplicados en el servidor. Cobros del mes (100, sin cortesía; tope de 300 generados),
/// carrito de Pro, historial de 30 días, recibo sin WhatsApp, contador y umbrales tras cada pago.
/// </summary>
public class FreePlanLimitsTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    private const string CartUrl = "/app/cobrar/productos";

    // --- Cobro por monto y por celular --------------------------------------------------------------------------------

    [Fact]
    public async Task Gratuito_con_99_concretados_todavia_cobra()
    {
        var client = await FreeMerchantAsync(Email);
        await SeedSalesAsync(Email, 99, paidAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        var html = await GetHtmlAsync(client, "/app/cobrar");
        var sale = await ChargeAsync(client);

        Assert.Contains("data-charge-usage-state=\"NearLimit\"", html);
        Assert.Contains("Te queda 1 cobro este mes", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("cortesía", WebUtility.HtmlDecode(html));
        Assert.Equal(SaleStatus.Pending, sale.Status);
        Assert.Single(Factory.Gateway.CodiQrRequests);
    }

    [Fact]
    public async Task Gratuito_con_100_concretados_no_genera_QR_ni_push_ni_llama_a_Pey()
    {
        var client = await FreeMerchantAsync(Email);
        await SeedSalesAsync(Email, 100, paidAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var before = await SalesCountAsync();

        var qr = await PostFormAsync(client, TokenPage, new Dictionary<string, string>
        {
            ["Input.Amount"] = "150.00",
            ["Input.Concept"] = "2 tacos al pastor",
        }, postUrl: "/app/cobrar");
        var push = await PostFormAsync(client, TokenPage, new Dictionary<string, string>
        {
            ["Input.Method"] = nameof(ChargeMethod.PushNotification),
            ["Input.Amount"] = "150.00",
            ["Input.Concept"] = "2 tacos al pastor",
            ["Input.PhoneNumber"] = "55 1234 5678",
        }, postUrl: "/app/cobrar");
        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, "/app/cobrar"));

        foreach (var response in new[] { qr, push })
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/app/cobrar", Location(response));
        }

        Assert.Equal(before, await SalesCountAsync());
        Assert.Empty(Factory.Gateway.CodiQrRequests);
        Assert.Empty(Factory.Gateway.PushRequests);
        Assert.Contains("data-charge-blocked", html);
        Assert.Contains("Llegaste a tus cobros del mes", html);
        Assert.Contains($"el {SaleFormatsDate(MexicoTime.NextMonthStart(MexicoTime.Today(DateTimeOffset.UtcNow)))}", html);
        Assert.Contains("Usaste los 100 cobros del mes del plan gratuito", html);
        Assert.Contains("data-charge-usage-state=\"Exhausted\"", html);
        Assert.Contains("href=\"/app/plan\"", html);
        Assert.DoesNotContain("data-charge-form", html);
        Assert.DoesNotContain("cortesía", html);
    }

    [Fact]
    public async Task Gratuito_con_300_generados_en_el_mes_no_genera_mas()
    {
        var client = await FreeMerchantAsync(Email);
        await SeedSalesAsync(Email, 300, paidAt: null);

        var response = await PostFormAsync(client, TokenPage, new Dictionary<string, string>
        {
            ["Input.Amount"] = "150.00",
            ["Input.Concept"] = "2 tacos al pastor",
        }, postUrl: "/app/cobrar");
        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, "/app/cobrar"));

        Assert.Equal("/app/cobrar", Location(response));
        Assert.Empty(Factory.Gateway.CodiQrRequests);
        Assert.Contains("data-charge-usage-state=\"GenerationCapReached\"", html);
        Assert.Contains("Llegaste al máximo de cobros generados", html);
    }

    [Fact]
    public async Task Los_concretados_del_mes_anterior_no_cuentan_y_los_del_dia_1_si()
    {
        var monthStart = MexicoTime.MonthRange(MexicoTime.Today(DateTimeOffset.UtcNow)).StartUtc;
        var client = await FreeMerchantAsync(Email);
        await SeedSalesAsync(Email, 100, paidAt: monthStart.AddSeconds(-1));

        var lastMonth = await ChargeAsync(client);
        await SeedSalesAsync(Email, 99, paidAt: monthStart); // 99 el día 1 a medianoche + 1 después = 100
        await SeedSalesAsync(Email, 1, paidAt: monthStart.AddMinutes(1));
        var blocked = await PostFormAsync(client, TokenPage, new Dictionary<string, string>
        {
            ["Input.Amount"] = "150.00",
            ["Input.Concept"] = "2 tacos al pastor",
        }, postUrl: "/app/cobrar");

        Assert.Equal(SaleStatus.Pending, lastMonth.Status);
        Assert.Equal("/app/cobrar", Location(blocked));
        Assert.Single(Factory.Gateway.CodiQrRequests);
    }

    [Fact]
    public async Task Los_concretados_de_otro_vendedor_no_cuentan()
    {
        await FreeMerchantAsync(OtherEmail);
        await SeedSalesAsync(OtherEmail, 100, paidAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var client = await FreeMerchantAsync(Email);

        var sale = await ChargeAsync(client);
        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, "/app/cobrar"));

        Assert.Equal(SaleStatus.Pending, sale.Status);
        Assert.Contains("0 de 100 cobros este mes", html);
    }

    [Fact]
    public async Task En_la_prueba_no_hay_limite_ni_contador()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedTrialAsync(Email);
        await SeedSalesAsync(Email, 200, paidAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        var html = await GetHtmlAsync(client, "/app/cobrar");
        var sale = await ChargeAsync(client);

        Assert.DoesNotContain("data-charge-usage", html);
        Assert.Equal(SaleStatus.Pending, sale.Status);
    }

    // --- Contador -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Inicio_y_Cobrar_muestran_el_contador_del_gratuito()
    {
        var client = await FreeMerchantAsync(Email);
        await SeedSalesAsync(Email, 82, paidAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        var home = WebUtility.HtmlDecode(await GetHtmlAsync(client, "/app"));
        var charge = WebUtility.HtmlDecode(await GetHtmlAsync(client, "/app/cobrar"));

        foreach (var html in new[] { home, charge })
        {
            Assert.Contains("82 de 100 cobros este mes", html);
            Assert.Contains("data-charge-usage-state=\"NearLimit\"", html);
            Assert.Contains("Te quedan 18 cobros este mes", html);
        }

        Assert.Contains("data-plan-tier=\"Expired\"", home);
        Assert.Contains("href=\"/app/plan\"", home);
    }

    [Fact]
    public async Task Cobrar_en_el_gratuito_recuerda_el_catalogo_que_espera()
    {
        // 5.10, lo que te pierdes: sin productos no hay línea; con productos, «Tu catálogo de N productos te espera».
        var client = await FreeMerchantAsync(Email);
        Assert.DoesNotContain("data-charge-missing-catalog", await GetHtmlAsync(client, "/app/cobrar"));

        await CreateProductAsync(Email, "Taco al pastor", 25m);
        await CreateProductAsync(Email, "Refresco", 20m);
        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, "/app/cobrar"));

        Assert.Contains("data-charge-missing-catalog", html);
        Assert.Contains("Tu catálogo de 2 productos te espera.", html);
    }

    [Fact]
    public async Task En_la_prueba_Cobrar_no_muestra_el_catalogo_que_espera()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedTrialAsync(Email);
        await CreateProductAsync(Email, "Taco al pastor", 25m);

        Assert.DoesNotContain("data-charge-missing-catalog", await GetHtmlAsync(client, "/app/cobrar"));
    }

    // --- Carrito ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Gratuito_no_cobra_con_carrito_ni_llama_a_Pey()
    {
        var client = await FreeMerchantAsync(Email);
        var tacoId = await CreateProductAsync(Email, "Taco al pastor", 25m);
        await CreateProductAsync(Email, "Refresco", 20m);

        var page = WebUtility.HtmlDecode(await GetHtmlAsync(client, CartUrl));
        var catalog = await client.GetAsync($"{CartUrl}/catalogo");
        var response = await PostFormAsync(client, TokenPage, new Dictionary<string, string>
        {
            ["Input.Concept"] = "Taco al pastor",
            ["Input.ExpectedTotal"] = "25.00",
            ["Input.Lines[0].ProductId"] = tacoId.ToString(CultureInfo.InvariantCulture),
            ["Input.Lines[0].Quantity"] = "1",
        }, postUrl: CartUrl);

        Assert.Contains("data-cart-pro-required", page);
        Assert.Contains("Tu catálogo de 2 productos te espera", page);
        Assert.DoesNotContain("data-cart-form", page);
        Assert.DoesNotContain("/js/cart", page);
        Assert.Equal(HttpStatusCode.Forbidden, catalog.StatusCode);
        Assert.Equal(CartUrl, Location(response));
        Assert.Equal(0, await SalesCountAsync());
        Assert.Empty(Factory.Gateway.CodiQrRequests);
    }

    [Fact]
    public async Task Una_venta_con_carrito_de_antes_de_bajar_al_gratuito_se_sigue_pagando()
    {
        await FreeMerchantAsync(Email);
        var tacoId = await CreateProductAsync(Email, "Taco al pastor", 25m);
        var sale = await SeedCartSaleAsync(Email, tacoId);

        var response = await PostWebhookAsync(QrPaymentBodyFor(sale));

        Assert.Equal("Processed", await WebhookStatusAsync(response));
        Assert.Equal(SaleStatus.Paid, (await LoadSaleAsync(sale.Id)).Status);
    }

    // --- Historial ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Gratuito_ve_30_dias_cuenta_las_ocultas_e_ignora_las_fechas()
    {
        var client = await FreeMerchantAsync(Email);
        var now = DateTimeOffset.UtcNow;
        var recent = (await SeedSalesAsync(Email, 1, paidAt: null, createdAt: now.AddDays(-2), concept: "Reciente"))[0];
        var old = await SeedSalesAsync(Email, 3, paidAt: null, createdAt: now.AddDays(-45), concept: "Vieja");

        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, "/app/ventas"));
        var withDates = WebUtility.HtmlDecode(await GetHtmlAsync(client,
            $"/app/ventas?desde={SaleFormats.IsoDate(MexicoTime.Today(now).AddDays(-60))}&hasta={SaleFormats.IsoDate(MexicoTime.Today(now))}"));

        Assert.Contains($"data-sale-row=\"{recent}\"", html);
        Assert.All(old, id => Assert.DoesNotContain($"data-sale-row=\"{id}\"", html));
        Assert.Contains("data-sales-hidden-older=\"3\"", html);
        Assert.Contains("Tienes 3 ventas de hace más de 30 días ocultas", html);
        Assert.Contains("data-date-filter-pro", html);
        Assert.DoesNotContain("data-date-filter>", html);
        Assert.All(old, id => Assert.DoesNotContain($"data-sale-row=\"{id}\"", withDates));
    }

    [Fact]
    public async Task Gratuito_abre_el_detalle_de_una_venta_vieja_con_el_aviso_de_Pro()
    {
        var client = await FreeMerchantAsync(Email);
        var now = DateTimeOffset.UtcNow;
        var oldId = (await SeedSalesAsync(Email, 1, paidAt: now.AddDays(-45), createdAt: now.AddDays(-45)))[0];
        var recentId = (await SeedSalesAsync(Email, 1, paidAt: now.AddDays(-1), createdAt: now.AddDays(-1)))[0];

        var old = await client.GetAsync($"/app/ventas/{oldId}");
        var oldHtml = WebUtility.HtmlDecode(await old.Content.ReadAsStringAsync());
        var qrPage = await client.GetAsync(SaleUrl(oldId));
        var recent = await GetHtmlAsync(client, $"/app/ventas/{recentId}");

        Assert.Equal(HttpStatusCode.OK, old.StatusCode);
        Assert.Contains("data-sale-locked", oldHtml);
        Assert.Contains("data-pro-required", oldHtml);
        Assert.Contains("Esta venta es de hace más de 30 días", oldHtml);
        Assert.Equal(HttpStatusCode.Redirect, qrPage.StatusCode);
        Assert.Equal($"/app/ventas/{oldId}", Location(qrPage));
        Assert.Contains($"data-sale-detail=\"{recentId}\"", recent);
    }

    [Fact]
    public async Task En_la_prueba_el_historial_es_completo()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedTrialAsync(Email);
        var old = (await SeedSalesAsync(Email, 1, paidAt: null, createdAt: DateTimeOffset.UtcNow.AddDays(-45)))[0];

        var html = await GetHtmlAsync(client, "/app/ventas");

        Assert.Contains($"data-sale-row=\"{old}\"", html);
        Assert.DoesNotContain("data-sales-hidden-older", html);
        Assert.Contains("data-date-filter", html);
        Assert.DoesNotContain("data-date-filter-pro", html);
    }

    // --- Recibo -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Gratuito_solo_copia_la_liga_del_recibo()
    {
        var client = await FreeMerchantAsync(Email);
        var sale = await ChargeByPhoneAsync(client);
        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(PushPaymentBodyFor(sale))));
        var paid = await LoadSaleAsync(sale.Id);

        var detail = WebUtility.HtmlDecode(await GetHtmlAsync(client, $"/app/ventas/{sale.Id}"));
        var whatsApp = await client.GetAsync($"/app/ventas/{sale.Id}/whatsapp");
        var receipt = await CreateClient().GetAsync($"/recibo/{paid.ReceiptToken}");

        Assert.Contains("data-receipt-share-limited", detail);
        Assert.Contains("data-receipt-copy", detail);
        Assert.Contains("data-receipt-url-field", detail);
        Assert.DoesNotContain("wa.me", detail);
        Assert.DoesNotContain("data-receipt-whatsapp", detail);
        Assert.DoesNotContain("data-receipt-native-share", detail);
        Assert.Equal(HttpStatusCode.Redirect, whatsApp.StatusCode);
        Assert.Equal($"/app/ventas/{sale.Id}", Location(whatsApp));
        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
    }

    [Fact]
    public async Task En_la_prueba_el_recibo_va_por_WhatsApp()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedTrialAsync(Email);
        var sale = await ChargeAsync(client);
        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(QrPaymentBodyFor(sale))));

        var detail = await GetHtmlAsync(client, $"/app/ventas/{sale.Id}");

        Assert.Contains("data-receipt-whatsapp", detail);
        Assert.DoesNotContain("data-receipt-share-limited", detail);
    }

    // --- Umbrales tras cada pago -------------------------------------------------------------------------------------

    [Fact]
    public async Task El_pago_80_del_mes_avisa_del_umbral_80()
    {
        var client = await FreeMerchantAsync(Email);
        await SeedSalesAsync(Email, 79, paidAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var sale = await ChargeAsync(client);

        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(QrPaymentBodyFor(sale))));

        Assert.Equal(new[] { (await MerchantIdAsync(Email), 80) }, Factory.LimitNotifier.FreeThresholds.ToArray());
    }

    [Fact]
    public async Task El_pago_100_del_mes_avisa_del_limite()
    {
        var client = await FreeMerchantAsync(Email);
        await SeedSalesAsync(Email, 99, paidAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var sale = await ChargeAsync(client);

        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(QrPaymentBodyFor(sale))));

        Assert.Equal(new[] { (await MerchantIdAsync(Email), 100) }, Factory.LimitNotifier.FreeThresholds.ToArray());
    }

    [Fact]
    public async Task Un_pago_adicional_de_una_venta_ya_pagada_no_vuelve_a_avisar()
    {
        var client = await FreeMerchantAsync(Email);
        await SeedSalesAsync(Email, 79, paidAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var sale = await ChargeAsync(client);

        await PostWebhookAsync(QrPaymentBodyFor(sale));
        await PostWebhookAsync(QrPaymentBodyFor(sale)); // otra transacción: pago adicional

        Assert.Single(Factory.LimitNotifier.FreeThresholds);
    }

    [Fact]
    public async Task Si_el_aviso_falla_el_pago_queda_guardado()
    {
        var client = await FreeMerchantAsync(Email);
        await SeedSalesAsync(Email, 99, paidAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var sale = await ChargeAsync(client);
        Factory.LimitNotifier.FailWith = new InvalidOperationException("Correo caído");

        var response = await PostWebhookAsync(QrPaymentBodyFor(sale));

        Assert.Equal("Processed", await WebhookStatusAsync(response));
        Assert.Equal(SaleStatus.Paid, (await LoadSaleAsync(sale.Id)).Status);
        Assert.Equal(1, await WithDbAsync(db => db.Payments.IgnoreQueryFilters().CountAsync(p => p.SaleId == sale.Id)));
    }

    [Fact]
    public async Task En_la_prueba_no_avisa_de_los_umbrales_del_gratuito()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedTrialAsync(Email);
        await SeedSalesAsync(Email, 79, paidAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var sale = await ChargeAsync(client);

        await PostWebhookAsync(QrPaymentBodyFor(sale));

        Assert.Empty(Factory.LimitNotifier.FreeThresholds);
        Assert.Empty(Factory.LimitNotifier.ProFairUse);
    }

    // --- Mi plan ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Mi_plan_muestra_el_plan_el_contador_y_lo_que_incluye_Pro()
    {
        var client = await FreeMerchantAsync(Email);
        await SeedSalesAsync(Email, 12, paidAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, "/app/plan"));
        var more = await GetHtmlAsync(client, "/app/mas");

        Assert.Contains("data-plan-tier=\"Expired\"", html);
        Assert.Contains("12 de 100 cobros este mes", html);
        Assert.Contains("data-plan-features", html);
        Assert.Contains("<td>100</td>", html);
        Assert.DoesNotContain("cortesía", html);
        Assert.Contains("data-coming-soon", html);
        Assert.Contains("Catálogo en línea con liga", html);
        Assert.Contains("Liga para copiar, con «Cobrado con Local133»", html);
        Assert.Contains("data-plan-offers", html);
        Assert.Contains("data-more-plan", more);
        Assert.Contains("data-more-static-qr", more);
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

    // --- Ayudas -------------------------------------------------------------------------------------------------------

    /// <summary>Vendedor activo, con cuenta bancaria activa y en el plan gratuito (su prueba ya terminó).</summary>
    private async Task<HttpClient> FreeMerchantAsync(string email)
    {
        var client = await ActiveMerchantAsync(email);
        await SeedFreePlanAsync(email);
        return client;
    }

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
                var sale = Sale.StartDirect(merchantId, folio + i, 10m, $"Tacos Don Ramón · {concept}", created.AddMinutes(10), created);
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

    /// <summary>Venta con carrito pendiente, con su QR, como si se hubiera creado antes de bajar al gratuito.</summary>
    private Task<Sale> SeedCartSaleAsync(string email, int productId) => WithDbAsync(async db =>
    {
        var merchantId = (await MerchantOfAsync(db, email)).Id;
        var product = await db.Products.IgnoreQueryFilters().SingleAsync(p => p.Id == productId);
        var now = DateTimeOffset.UtcNow;
        var sale = Sale.StartDirect(merchantId, 1, product.Price, "Tacos Don Ramón · Taco al pastor", now.AddMinutes(10), now,
            [SaleItem.FromProduct(product, 1)]);
        sale.AttachQr("IDC-CARRITO-1", FakePaymentGateway.QrStringFor("IDC-CARRITO-1"));
        db.Sales.Add(sale);
        await db.SaveChangesAsync();
        return sale;
    });

    private Task<int> SalesCountAsync() => WithDbAsync(db => db.Sales.IgnoreQueryFilters().CountAsync());

    private static string SaleFormatsDate(DateOnly day) =>
        SaleFormats.Date(day, MexicoTime.Today(DateTimeOffset.UtcNow));
}
