using System.Net;
using System.Text.RegularExpressions;
using Local133.Domain.Sales;
using Local133.Domain.Subscriptions;
using Local133.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Local133.IntegrationTests.Admin;

/// <summary>6.4: transacciones globales del admin (/admin/transacciones).</summary>
public partial class TransactionsTests(Local133WebFactory factory) : AdminTestBase(factory)
{
    private const string Url = "/admin/transacciones";

    [Theory]
    [InlineData(Url)]
    [InlineData(Url + "/suscripciones")]
    [InlineData(Url + "/ventas/1")]
    public async Task Un_vendedor_no_entra(string path)
    {
        var client = await ActiveMerchantAsync(Email);

        Assert.StartsWith("/Identity/Account/AccessDenied", Location(await client.GetAsync(path)));
    }

    [Fact]
    public async Task Lista_las_ventas_de_todos_los_vendedores_con_filtros()
    {
        var now = DateTimeOffset.UtcNow;
        await ActiveMerchantAsync(Email);
        await ActiveMerchantAsync(OtherEmail);
        await UpdateMerchantAsync(OtherEmail, m => m.BusinessName = "Jugos La Güera");
        var paid = await InsertSaleAsync(Email, 150m, now.AddHours(-3), concept: "Tacos Don Ramón · Orden de pastor");
        await AddPaymentAsync(paid, 150m, now.AddHours(-3).AddMinutes(1));
        var push = await InsertSaleAsync(OtherEmail, 40m, now.AddHours(-2), expiresAt: now.AddMinutes(5), phone: "5512345678",
            concept: "Jugos La Güera · Jugo verde");
        var expired = await InsertSaleAsync(Email, 30m, now.AddHours(-1), expiresAt: now.AddMinutes(-50));
        var otherId = await MerchantIdAsync(OtherEmail);
        var admin = await AdminAsync();

        var all = WebUtility.HtmlDecode(await GetHtmlAsync(admin, Url));

        Assert.Equal([expired.Id, push.Id, paid.Id], RowIds(all));
        Assert.Contains("Jugos La Güera", all);
        Assert.Contains("Orden de pastor", all);
        Assert.DoesNotContain("5512345678", all);
        Assert.Contains(PhoneNumber.Mask("5512345678"), all);
        Assert.Contains("data-transactions-collected", all);
        Assert.Contains("$150.00", all);
        Assert.Matches(new Regex("class=\"nav-link admin-nav-link active\" href=\"/admin/transacciones\""), all);

        Assert.Equal([paid.Id], RowIds(await GetHtmlAsync(admin, Url + "?estado=pagadas")));
        Assert.Equal([expired.Id], RowIds(await GetHtmlAsync(admin, Url + "?estado=vencidas")));
        Assert.Equal([push.Id], RowIds(await GetHtmlAsync(admin, Url + "?metodo=celular")));
        Assert.Equal([push.Id], RowIds(await GetHtmlAsync(admin, $"{Url}?cliente={otherId}")));
        Assert.Equal([push.Id], RowIds(await GetHtmlAsync(admin, Url + "?q=" + Uri.EscapeDataString("güera"))));
        Assert.Equal([push.Id], RowIds(await GetHtmlAsync(admin, Url + "?q=" + push.PeyTransactionId)));
        Assert.Equal([paid.Id], RowIds(await GetHtmlAsync(admin, Url + "?q=TRK123"))); // clave de rastreo del pago
        Assert.Equal([paid.Id], RowIds(await GetHtmlAsync(admin, Url + "?q=" + paid.PeyQrId)));
        Assert.Empty(RowIds(await GetHtmlAsync(admin, Url + "?q=%25")));
    }

    [Fact]
    public async Task Busca_por_folio_en_todos_los_vendedores()
    {
        var now = DateTimeOffset.UtcNow;
        await ActiveMerchantAsync(Email);
        await ActiveMerchantAsync(OtherEmail);
        var first = await InsertSaleAsync(Email, 10m, now.AddMinutes(-3));
        var second = await InsertSaleAsync(Email, 10m, now.AddMinutes(-2));
        var otherFirst = await InsertSaleAsync(OtherEmail, 10m, now.AddMinutes(-1));
        var admin = await AdminAsync();

        Assert.Equal([otherFirst.Id, first.Id], RowIds(await GetHtmlAsync(admin, Url + "?q=%231")));
        Assert.Equal([second.Id], RowIds(await GetHtmlAsync(admin, Url + "?q=2")));
    }

    [Fact]
    public async Task Pagina_de_veinte_en_veinte()
    {
        var now = DateTimeOffset.UtcNow;
        await ActiveMerchantAsync(Email);
        for (var i = 0; i < 21; i++)
        {
            await InsertSaleAsync(Email, 10m, now.AddMinutes(-i));
        }

        var admin = await AdminAsync();

        var first = await GetHtmlAsync(admin, Url);
        Assert.Equal(20, RowIds(first).Count);
        Assert.Contains("data-transactions-count=\"21\"", first);
        Assert.Single(RowIds(await GetHtmlAsync(admin, Url + "?pagina=2")));
    }

    [Fact]
    public async Task Detalle_de_una_venta_con_productos_pagos_y_webhooks()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client, concept: "Mesa 4");
        var transactionId = NewTransactionId();
        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(QrPaymentBodyFor(sale, transactionId))));
        var webhookId = await WithDbAsync(db => db.WebhookEvents.AsNoTracking().Select(e => e.Id).SingleAsync());
        var cart = await InsertSaleAsync(Email, 0m, DateTimeOffset.UtcNow, items: [("Taco al pastor", 25m, 2), ("Refresco", 20m, 1)]);
        var admin = await AdminAsync();

        var html = WebUtility.HtmlDecode(await GetHtmlAsync(admin, $"{Url}/ventas/{sale.Id}"));
        var cartHtml = WebUtility.HtmlDecode(await GetHtmlAsync(admin, $"{Url}/ventas/{cart.Id}"));

        Assert.Contains("data-sale-status=\"Paid\"", html);
        Assert.Contains("Mesa 4", html);
        Assert.Contains($"data-sale-payment=\"{transactionId}\"", html);
        Assert.Contains($"data-sale-webhook=\"{webhookId}\"", html);
        Assert.Contains(sale.PeyQrId!, html);
        Assert.DoesNotContain(TestClabe, html);
        Assert.Contains("Taco al pastor", cartHtml);
        Assert.Contains("data-sale-no-payments", cartHtml);
        Assert.Contains("data-sale-no-webhooks", cartHtml);
        Assert.Matches(new Regex("class=\"nav-link admin-nav-link active\" href=\"/admin/transacciones\""), html);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Url}/ventas/999999")).StatusCode);
    }

    [Fact]
    public async Task Pagos_de_la_suscripcion_con_filtros()
    {
        var now = DateTimeOffset.UtcNow;
        await ActiveMerchantAsync(Email);
        await SeedTrialAsync(Email);
        await ActiveMerchantAsync(OtherEmail);
        await UpdateMerchantAsync(OtherEmail, m => m.BusinessName = "Jugos La Güera");
        await SeedTrialAsync(OtherEmail);
        var codiCharge = await InsertChargeAsync(Email, 199m, now.AddDays(-3), paidAt: now.AddDays(-3));
        var codi = await InsertSubscriptionPaymentAsync(codiCharge, "CODI", 199m, now.AddDays(-3));
        var cardCharge = await InsertChargeAsync(OtherEmail, 149m, now.AddDays(-1), paidAt: now.AddDays(-1));
        var card = await InsertSubscriptionPaymentAsync(cardCharge, "CARD", 149m, now.AddDays(-1), fee: 5.5m);
        var admin = await AdminAsync();

        var all = WebUtility.HtmlDecode(await GetHtmlAsync(admin, Url + "/suscripciones"));

        Assert.Equal([card.Id, codi.Id], PaymentIds(all));
        Assert.Contains("data-subscription-payments-count=\"2\"", all);
        Assert.Contains("comisión $5.50", all);
        Assert.Contains("Pro, 1 mes", all);
        Assert.Contains("data-transaction-tab=\"Subscriptions\"", all);
        Assert.Equal([card.Id], PaymentIds(await GetHtmlAsync(admin, Url + "/suscripciones?metodo=tarjeta")));
        Assert.Equal([codi.Id], PaymentIds(await GetHtmlAsync(admin, Url + "/suscripciones?q=" + codi.PeyTransactionId)));
        Assert.Equal([codi.Id], PaymentIds(await GetHtmlAsync(admin, $"{Url}/suscripciones?q=%23{codiCharge.Id}")));
        Assert.Equal([card.Id], PaymentIds(await GetHtmlAsync(admin, Url + "/suscripciones?q=" + cardCharge.PeyPaymentLinkId)));
        Assert.Equal([card.Id], PaymentIds(await GetHtmlAsync(admin, Url + "/suscripciones?q=jugos")));
    }

    private static List<int> RowIds(string html) =>
        RowRegex().Matches(html).Select(m => int.Parse(m.Groups[1].Value)).ToList();

    private static List<int> PaymentIds(string html) =>
        PaymentRegex().Matches(html).Select(m => int.Parse(m.Groups[1].Value)).ToList();

    [GeneratedRegex("data-transaction-row=\"(\\d+)\"")]
    private static partial Regex RowRegex();

    [GeneratedRegex("data-subscription-payment-row=\"(\\d+)\"")]
    private static partial Regex PaymentRegex();
}
