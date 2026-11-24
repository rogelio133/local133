using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Local133.Domain.Common;
using Local133.Domain.Merchants;
using Local133.Domain.Payments;
using Local133.Domain.Pey;
using Local133.Domain.Sales;
using Local133.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Local133.IntegrationTests.Sales;

/// <summary>Paso 2.8 (+ 3.3): historial de ventas, resumen del día, detalle con productos y pagos, y ligas del recibo.</summary>
public class SalesHistoryTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    private const string Url = "/app/ventas";

    // --- Historial --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Sin_ventas_muestra_el_aviso_y_el_resumen_en_cero()
    {
        var client = await ActiveMerchantAsync(Email);

        var html = await GetHtmlAsync(client, Url);

        Assert.Contains("data-sales-empty", html);
        Assert.Contains("data-day-collected>$0.00<", html);
        Assert.Contains("data-day-paid>0<", html);
        Assert.Contains("data-day-pending>0<", html);
        Assert.Matches(new Regex("class=\"nav-link active\" href=\"/app/ventas\""), html); // pestaña «Ventas»
        Assert.DoesNotContain("data-status-filters", html);
    }

    [Fact]
    public async Task Lista_solo_las_ventas_del_vendedor_de_la_mas_reciente_a_la_mas_antigua()
    {
        var client = await ActiveMerchantAsync(Email);
        await ActiveMerchantAsync(OtherEmail);
        var now = DateTimeOffset.UtcNow;
        var older = await InsertSaleAsync(Email, 10m, now.AddMinutes(-30), concept: "Tacos Don Ramón · Refresco");
        var newer = await InsertSaleAsync(Email, 20m, now.AddMinutes(-5), concept: "Tacos Don Ramón · 2 tacos al pastor");
        var foreign = await InsertSaleAsync(OtherEmail, 30m, now.AddMinutes(-1));

        var html = await GetHtmlAsync(client, Url);

        Assert.Contains("data-sales-count>", html);
        Assert.Contains("2 ventas", html);
        Assert.True(html.IndexOf($"data-sale-row=\"{newer.Id}\"", StringComparison.Ordinal)
            < html.IndexOf($"data-sale-row=\"{older.Id}\"", StringComparison.Ordinal));
        Assert.DoesNotContain($"data-sale-row=\"{foreign.Id}\"", html);
        Assert.Contains($"href=\"/app/ventas/{newer.Id}\"", html);
        Assert.Contains("data-sales-day>Hoy<", html);
        // El concepto se muestra sin el nombre del negocio (ya es su propio historial).
        Assert.Contains(">2 tacos al pastor<", html);
        Assert.DoesNotContain(">Tacos Don Ramón · 2 tacos al pastor<", html);
    }

    [Fact]
    public async Task Filtra_por_estatus_efectivo()
    {
        var client = await ActiveMerchantAsync(Email);
        var now = DateTimeOffset.UtcNow;
        var pending = await InsertSaleAsync(Email, 10m, now.AddMinutes(-1), expiresAt: now.AddMinutes(9));
        var overdue = await InsertSaleAsync(Email, 11m, now.AddMinutes(-20), expiresAt: now.AddMinutes(-10)); // pendiente vencida
        var expired = await InsertSaleAsync(Email, 12m, now.AddMinutes(-30), status: SaleStatus.Expired, expiresAt: now.AddMinutes(-20));
        var cancelled = await InsertSaleAsync(Email, 13m, now.AddMinutes(-40), status: SaleStatus.Cancelled);
        var paid = await InsertSaleAsync(Email, 14m, now.AddMinutes(-50));
        await AddPaymentAsync(paid, 14m, now.AddMinutes(-49));

        async Task<int[]> IdsAsync(string query)
        {
            var html = await GetHtmlAsync(client, Url + query);
            return Regex.Matches(html, "data-sale-row=\"(\\d+)\"").Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();
        }

        Assert.Equal([pending.Id], await IdsAsync("?estado=pendientes"));
        Assert.Equal([overdue.Id, expired.Id], await IdsAsync("?estado=vencidas"));
        Assert.Equal([cancelled.Id], await IdsAsync("?estado=canceladas"));
        Assert.Equal([paid.Id], await IdsAsync("?estado=pagadas"));
        Assert.Equal(5, (await IdsAsync("?estado=otra")).Length);

        var all = await GetHtmlAsync(client, Url);
        Assert.Matches(new Regex($"data-sale-row=\"{overdue.Id}\" data-status=\"Expired\""), all);
        var filtered = await GetHtmlAsync(client, Url + "?estado=pagadas");
        Assert.Contains("aria-current=\"true\" data-status-filter=\"pagadas\"", filtered);
        Assert.Contains("data-clear-filters", filtered);
    }

    [Fact]
    public async Task Filtra_por_dias_en_hora_de_Mexico()
    {
        var client = await ActiveMerchantAsync(Email);
        var now = DateTimeOffset.UtcNow;
        var today = MexicoTime.Today(now);
        var todays = await InsertSaleAsync(Email, 10m, MexicoTime.DayRange(today).StartUtc.AddMinutes(1));
        var threeDaysAgo = await InsertSaleAsync(Email, 20m, MexicoTime.DayRange(today.AddDays(-3)).StartUtc.AddHours(12));
        var iso = (DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var onlyToday = await GetHtmlAsync(client, $"{Url}?desde={iso(today)}&hasta={iso(today)}");
        Assert.Contains($"data-sale-row=\"{todays.Id}\"", onlyToday);
        Assert.DoesNotContain($"data-sale-row=\"{threeDaysAgo.Id}\"", onlyToday);
        Assert.Contains("open=\"open\" data-date-filter", onlyToday);
        Assert.Contains("data-clear-dates", onlyToday);

        // Fechas invertidas: se intercambian.
        var inverted = await GetHtmlAsync(client, $"{Url}?desde={iso(today.AddDays(-2))}&hasta={iso(today.AddDays(-4))}");
        Assert.Contains($"data-sale-row=\"{threeDaysAgo.Id}\"", inverted);
        Assert.DoesNotContain($"data-sale-row=\"{todays.Id}\"", inverted);

        // Una fecha ilegible se ignora.
        var invalid = await GetHtmlAsync(client, $"{Url}?desde=ayer&hasta=2026-13-40");
        Assert.Contains($"data-sale-row=\"{todays.Id}\"", invalid);
        Assert.Contains($"data-sale-row=\"{threeDaysAgo.Id}\"", invalid);

        var none = await GetHtmlAsync(client, $"{Url}?desde={iso(today.AddDays(-10))}&hasta={iso(today.AddDays(-9))}");
        Assert.Contains("data-sales-no-results", none);
    }

    [Fact]
    public async Task Pagina_de_20_en_20()
    {
        var client = await ActiveMerchantAsync(Email);
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 25; i++)
        {
            await InsertSaleAsync(Email, 10m + i, now.AddMinutes(-i - 1), expiresAt: now.AddHours(1));
        }

        var first = await GetHtmlAsync(client, Url);
        var second = await GetHtmlAsync(client, Url + "?estado=pendientes&pagina=2");
        var beyond = await GetHtmlAsync(client, Url + "?pagina=99");

        Assert.Equal(20, Regex.Matches(first, "data-sale-row=").Count);
        Assert.Contains("data-pager", first);
        Assert.Contains("href=\"/app/ventas?pagina=2\"", first);
        Assert.Equal(5, Regex.Matches(second, "data-sale-row=").Count);
        Assert.Contains("href=\"/app/ventas?estado=pendientes\" rel=\"prev\"", second);
        Assert.Equal(5, Regex.Matches(beyond, "data-sale-row=").Count); // ajustada a la última
    }

    // --- Resumen del día ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task El_resumen_suma_los_pagos_de_hoy_y_cuenta_las_pendientes()
    {
        var client = await ActiveMerchantAsync(Email);
        var now = DateTimeOffset.UtcNow;
        var first = await InsertSaleAsync(Email, 100m, now.AddMinutes(-10));
        await AddPaymentAsync(first, 100m, now.AddMinutes(-9));
        var second = await InsertSaleAsync(Email, 50m, now.AddMinutes(-8));
        await AddPaymentAsync(second, 50m, now.AddMinutes(-7));
        await AddPaymentAsync(second, 50m, now.AddMinutes(-6)); // pago adicional del mismo QR
        var old = await InsertSaleAsync(Email, 999m, now.AddDays(-3));
        await AddPaymentAsync(old, 999m, now.AddDays(-3));
        await InsertSaleAsync(Email, 10m, now.AddMinutes(-1), expiresAt: now.AddMinutes(9));
        await InsertSaleAsync(Email, 10m, now.AddMinutes(-30), expiresAt: now.AddMinutes(-20)); // vencida: no cuenta
        await ActiveMerchantAsync(OtherEmail);
        var foreign = await InsertSaleAsync(OtherEmail, 500m, now.AddMinutes(-2));
        await AddPaymentAsync(foreign, 500m, now.AddMinutes(-2));

        var html = await GetHtmlAsync(client, Url);
        Assert.Contains("data-day-collected>$200.00<", html);
        Assert.Contains("data-day-paid>2<", html);
        Assert.Contains("data-day-pending>1<", html);

        var home = await GetHtmlAsync(client, "/app");
        Assert.Contains("data-day-summary", home);
        Assert.Contains("data-day-collected>$200.00<", home);
        Assert.Contains("2 ventas pagadas", home);
        Assert.Contains("1 esperando pago", home);
    }

    [Fact]
    public async Task El_inicio_no_muestra_el_resumen_durante_el_onboarding()
    {
        await CreateMerchantAsync(Email);
        var client = CreateClient();
        await LoginAsync(client, Email);

        var home = await GetHtmlAsync(client, "/app");

        Assert.DoesNotContain("data-day-summary", home);
    }

    [Fact]
    public async Task Un_vendedor_suspendido_consulta_su_historial()
    {
        var client = await ActiveMerchantAsync(Email, status: MerchantStatus.Suspended);
        var sale = await InsertSaleAsync(Email, 10m, DateTimeOffset.UtcNow.AddMinutes(-5));

        Assert.Contains($"data-sale-row=\"{sale.Id}\"", await GetHtmlAsync(client, Url));
        Assert.Contains("data-day-summary", await GetHtmlAsync(client, "/app"));
        Assert.Contains($"data-sale-detail=\"{sale.Id}\"", await GetHtmlAsync(client, $"{Url}/{sale.Id}"));
    }

    // --- Detalle ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Detalle_de_una_venta_con_productos_pagada()
    {
        var client = await ActiveMerchantAsync(Email);
        var tacoId = await CreateProductAsync(Email, "Taco al pastor", 25m);
        var response = await PostFormAsync(client, "/app/cobrar/productos", new Dictionary<string, string>
        {
            ["Input.Lines[0].ProductId"] = tacoId.ToString(CultureInfo.InvariantCulture),
            ["Input.Lines[0].Quantity"] = "6",
            ["Input.ExpectedTotal"] = "150.00",
            ["Input.Concept"] = "6 Taco al pastor",
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var sale = await LoadSaleAsync(int.Parse(Location(response)!.Split('/').Last(), CultureInfo.InvariantCulture));
        await PostWebhookAsync(QrPaymentBodyFor(sale));

        var html = await GetHtmlAsync(client, $"{Url}/{sale.Id}");

        Assert.Contains("data-status=\"Paid\"", html);
        Assert.Contains("data-sale-status>Pagada<", html);
        Assert.Contains("data-sale-amount>$150.00<", html);
        Assert.Contains("data-sale-concept>6 Taco al pastor<", html);
        Assert.Contains("con productos", html);
        // Productos (3.3): la copia guardada al cobrar.
        Assert.Contains("data-sale-items", html);
        Assert.Contains("Taco al pastor", html);
        Assert.Contains("6 × $25.00", html);
        // Pagos con clave de rastreo; sin comisión desde la 5.1.
        Assert.Contains("data-payment-tracking>0192191042<", html);
        Assert.DoesNotContain("Comisión", html);
        // Recibo para compartir.
        Assert.Contains("data-sale-receipt", html);
        Assert.Contains($"data-receipt-url=\"https://localhost/recibo/{sale.ReceiptToken}\"", html);
        Assert.Contains("href=\"https://wa.me/?text=", html);
        Assert.Contains(Uri.EscapeDataString($"https://localhost/recibo/{sale.ReceiptToken}"), html);
        Assert.Contains($"href=\"/recibo/{sale.ReceiptToken}\"", html);
        Assert.Contains("/js/receipt-share", html);
        Assert.Matches(new Regex("class=\"nav-link active\" href=\"/app/ventas\""), html);
    }

    [Fact]
    public async Task Detalle_de_una_pendiente_enlaza_al_QR_y_no_tiene_recibo()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);

        var html = await GetHtmlAsync(client, $"{Url}/{sale.Id}");

        Assert.Contains("data-sale-status>Pendiente<", html);
        Assert.Contains($"href=\"/app/cobrar/venta/{sale.Id}\" data-sale-charge-link", html);
        Assert.Contains("data-sale-no-payments", html);
        Assert.DoesNotContain("data-sale-receipt", html);
        Assert.DoesNotContain(sale.ReceiptToken, html);
        Assert.DoesNotContain("data-sale-items", html);
    }

    [Fact]
    public async Task Detalle_de_un_cobro_por_celular_pagado_manda_el_recibo_al_cliente()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeByPhoneAsync(client, "55 1234 5678");
        await PostWebhookAsync(PushPaymentBodyFor(sale));

        var html = await GetHtmlAsync(client, $"{Url}/{sale.Id}");

        Assert.Contains("data-sale-phone>(•••• 5678)<", html);
        Assert.Contains($"href=\"/app/ventas/{sale.Id}/whatsapp\"", html);
        Assert.Contains("Enviar recibo al <span class=\"text-nowrap\">•••• 5678<", html);
        Assert.Contains("href=\"https://wa.me/?text=", html); // «Enviar a otro contacto»
        // El celular completo nunca se escribe en la página.
        Assert.DoesNotContain("5512345678", html);

        var redirect = await client.GetAsync($"/app/ventas/{sale.Id}/whatsapp");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.StartsWith("https://wa.me/525512345678?text=", redirect.Headers.Location!.AbsoluteUri);
        Assert.Contains(Uri.EscapeDataString($"https://localhost/recibo/{sale.ReceiptToken}"), redirect.Headers.Location!.AbsoluteUri);
        Assert.True(redirect.Headers.CacheControl?.NoStore);

        // Escritorio: receipt-share.js cambia los botones a la app (whatsapp://) y ofrece WhatsApp Web.
        Assert.Contains($"data-desktop-href=\"/app/ventas/{sale.Id}/whatsapp?destino=app\"", html);
        Assert.Contains("data-desktop-href=\"whatsapp://send?text=", html);
        Assert.Contains($"href=\"/app/ventas/{sale.Id}/whatsapp?destino=web\"", html);
        var toApp = await client.GetAsync($"/app/ventas/{sale.Id}/whatsapp?destino=app");
        Assert.StartsWith("whatsapp://send?phone=525512345678&text=", toApp.Headers.Location!.OriginalString);
        var toWeb = await client.GetAsync($"/app/ventas/{sale.Id}/whatsapp?destino=web");
        Assert.StartsWith("https://web.whatsapp.com/send?phone=525512345678&text=", toWeb.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task La_redireccion_a_WhatsApp_exige_venta_propia_pagada_y_con_celular()
    {
        var client = await ActiveMerchantAsync(Email);
        var pendingPush = await ChargeByPhoneAsync(client, "55 1234 5678");
        var paidQr = await ChargeAsync(client);
        await PostWebhookAsync(QrPaymentBodyFor(paidQr));
        var other = await ActiveMerchantAsync(OtherEmail);
        var otherPush = await ChargeByPhoneAsync(other, "55 8765 4321");
        await PostWebhookAsync(PushPaymentBodyFor(otherPush));

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/app/ventas/{pendingPush.Id}/whatsapp")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/app/ventas/{paidQr.Id}/whatsapp")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/app/ventas/{otherPush.Id}/whatsapp")).StatusCode);
    }

    [Fact]
    public async Task El_detalle_de_otro_vendedor_da_404()
    {
        var client = await ActiveMerchantAsync(Email);
        await ActiveMerchantAsync(OtherEmail);
        var foreign = await InsertSaleAsync(OtherEmail, 10m, DateTimeOffset.UtcNow);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Url}/{foreign.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Url}/999999")).StatusCode);
    }

    [Fact]
    public async Task Sin_sesion_redirige_al_login()
    {
        var response = await CreateClient().GetAsync($"{Url}/1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Identity/Account/Login", Location(response));
    }

    // --- Pantalla de «Pago recibido» -----------------------------------------------------------------------------------

    [Fact]
    public async Task La_pantalla_de_la_venta_trae_el_recibo_oculto_hasta_que_se_paga()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);

        var pending = await GetHtmlAsync(client, SaleUrl(sale.Id));
        Assert.Contains("sale-receipt d-none\" data-paid-only", pending);
        Assert.Contains("data-receipt-whatsapp", pending);
        Assert.Contains("/js/receipt-share", pending);

        await PostWebhookAsync(QrPaymentBodyFor(sale));
        var paid = await GetHtmlAsync(client, SaleUrl(sale.Id));
        Assert.Contains("class=\"mx-auto mb-3 sale-receipt\" data-paid-only", paid);
        Assert.Contains($"data-receipt-url=\"https://localhost/recibo/{sale.ReceiptToken}\"", paid);
        Assert.Contains($"href=\"/app/ventas/{sale.Id}\" data-sale-detail-link", paid);
    }

    // --- Búsqueda (5.9) ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Busca_por_concepto_sin_acentos_ni_mayusculas_y_no_por_el_negocio()
    {
        var client = await ActiveMerchantAsync(Email);
        await ActiveMerchantAsync(OtherEmail);
        var now = DateTimeOffset.UtcNow;
        var platano = await InsertSaleAsync(Email, 10m, now.AddMinutes(-30), concept: "Tacos Don Ramón · Plátano frito");
        var refresco = await InsertSaleAsync(Email, 20m, now.AddMinutes(-20), concept: "Tacos Don Ramón · Refresco");
        var old = await InsertSaleAsync(Email, 30m, now.AddMinutes(-10), concept: "Agua de jamaica"); // sin negocio (antes de la 4.1)
        var foreign = await InsertSaleAsync(OtherEmail, 40m, now.AddMinutes(-5), concept: "Tacos Don Ramón · Plátano frito");

        var byConcept = await GetHtmlAsync(client, Url + "?q=PLATANO");
        Assert.Contains($"data-sale-row=\"{platano.Id}\"", byConcept);
        Assert.DoesNotContain($"data-sale-row=\"{refresco.Id}\"", byConcept);
        Assert.DoesNotContain($"data-sale-row=\"{foreign.Id}\"", byConcept);
        Assert.Contains("value=\"PLATANO\"", byConcept);
        Assert.Contains("data-clear-search", byConcept);
        Assert.Contains("data-clear-filters", byConcept);

        // El nombre del negocio va antes de cada concepto: buscarlo no debe traer todas las ventas.
        Assert.Contains("data-sales-no-results", await GetHtmlAsync(client, Url + "?q=Don%20Ram%C3%B3n"));

        var withoutBusiness = await GetHtmlAsync(client, Url + "?q=jamaica");
        Assert.Contains($"data-sale-row=\"{old.Id}\"", withoutBusiness);
        Assert.DoesNotContain($"data-sale-row=\"{platano.Id}\"", withoutBusiness);

        // Los comodines de LIKE se buscan como texto.
        Assert.Contains("data-sales-no-results", await GetHtmlAsync(client, Url + "?q=%25"));
    }

    [Fact]
    public async Task Busca_por_folio_y_combina_con_estatus_y_fechas()
    {
        var client = await ActiveMerchantAsync(Email);
        var now = DateTimeOffset.UtcNow;
        var first = await InsertSaleAsync(Email, 10m, now.AddMinutes(-30), concept: "Tacos Don Ramón · Mesa 2");
        var second = await InsertSaleAsync(Email, 20m, now.AddMinutes(-20), concept: "Tacos Don Ramón · Refresco");
        var third = await InsertSaleAsync(Email, 30m, now.AddMinutes(-10), concept: "Tacos Don Ramón · Refresco grande");
        await AddPaymentAsync(third, 30m, now.AddMinutes(-9));

        // «#2» es solo el folio; «2» también busca en el concepto («Mesa 2»).
        var byFolio = await GetHtmlAsync(client, Url + "?q=%232");
        Assert.Contains($"data-sale-row=\"{second.Id}\"", byFolio);
        Assert.DoesNotContain($"data-sale-row=\"{first.Id}\"", byFolio);
        var number = await GetHtmlAsync(client, Url + "?q=2");
        Assert.Contains($"data-sale-row=\"{first.Id}\"", number);
        Assert.Contains($"data-sale-row=\"{second.Id}\"", number);
        Assert.DoesNotContain($"data-sale-row=\"{third.Id}\"", number);

        var today = MexicoTime.Today(now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var paid = await GetHtmlAsync(client, $"{Url}?estado=pagadas&q=refresco&desde={today}&hasta={today}");
        Assert.Contains($"data-sale-row=\"{third.Id}\"", paid);
        Assert.DoesNotContain($"data-sale-row=\"{second.Id}\"", paid);
        // Los filtros conservan la búsqueda, y la búsqueda conserva el estatus y las fechas.
        Assert.Contains("q=refresco", paid);
        Assert.Contains($"name=\"desde\" value=\"{today}\"", paid);
        Assert.Contains($"href=\"/app/ventas/exportar?estado=pagadas&amp;desde={today}&amp;hasta={today}&amp;q=refresco\"", paid);
    }

    [Fact]
    public async Task El_gratuito_no_busca_ni_exporta_ni_tiene_corte()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedFreePlanAsync(Email);
        var now = DateTimeOffset.UtcNow;
        var platano = await InsertSaleAsync(Email, 10m, now.AddMinutes(-30), concept: "Tacos Don Ramón · Plátano frito");
        var refresco = await InsertSaleAsync(Email, 20m, now.AddMinutes(-20), concept: "Tacos Don Ramón · Refresco");

        var html = await GetHtmlAsync(client, Url + "?q=platano");

        Assert.Contains($"data-sale-row=\"{platano.Id}\"", html);
        Assert.Contains($"data-sale-row=\"{refresco.Id}\"", html);
        Assert.DoesNotContain("data-sales-search", html);
        Assert.DoesNotContain("data-sales-export", html);
        Assert.Contains("data-date-filter-pro", html);
        Assert.Contains("data-cash-cut-pro", html);
    }
}
