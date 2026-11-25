using System.Net;
using System.Text.RegularExpressions;
using Local133.Application.Sales;
using Local133.Domain.Audit;
using Local133.Domain.BankAccounts;
using Local133.Domain.Pey;
using Local133.Domain.Webhooks;
using Local133.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Local133.IntegrationTests.Admin;

/// <summary>6.4: bitácora de webhooks del admin (/admin/webhooks), con el payload enmascarado y el reproceso.</summary>
public partial class WebhookLogTests(Local133WebFactory factory) : AdminTestBase(factory)
{
    private const string Url = "/admin/webhooks";

    [Fact]
    public async Task Un_vendedor_no_entra_ni_reprocesa()
    {
        var webhook = await InsertWebhookEventAsync(PeyWebhookEventTypes.CodiQrPaymentReceived, "{}", DateTimeOffset.UtcNow, WebhookEventStatus.Failed);
        var client = await ActiveMerchantAsync(Email);

        var get = await client.GetAsync($"{Url}/{webhook.Id}");
        var post = await PostFormAsync(client, "/app", new Dictionary<string, string> { ["Confirm.Note"] = "x" }, $"{Url}/{webhook.Id}/reprocesar");

        Assert.StartsWith("/Identity/Account/AccessDenied", Location(get));
        Assert.StartsWith("/Identity/Account/AccessDenied", Location(post));
        Assert.Equal(1, (await LoadAsync(webhook.Id)).Attempts);
    }

    [Fact]
    public async Task Un_pago_fallido_se_reprocesa_cuando_ya_existe_la_venta()
    {
        await ActiveMerchantAsync(Email);
        var idc = "IDCREPROCESO00000001";
        var transactionId = NewTransactionId();
        var body = QrPaymentBody(transactionId, idc, 50m, external: null);
        Assert.Equal("Failed", await WebhookStatusAsync(await PostWebhookAsync(body)));
        var failed = await SingleWebhookAsync();
        var sale = await InsertSaleAsync(Email, 50m, DateTimeOffset.UtcNow.AddMinutes(-5));
        await UpdateSaleAsync(sale.Id, s => s.PeyQrId = idc);
        var admin = await AdminAsync();
        var detailUrl = $"{Url}/{failed.Id}";

        var before = await GetHtmlAsync(admin, detailUrl);
        var response = await PostFormAsync(admin, detailUrl, new Dictionary<string, string> { ["Confirm.Note"] = "Venta registrada después" },
            $"{detailUrl}/reprocesar");

        Assert.Contains("data-webhook-reprocess", before);
        Assert.Contains("data-webhook-no-history", before);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(detailUrl, Location(response));
        var reprocessed = await LoadAsync(failed.Id);
        Assert.Equal(WebhookEventStatus.Processed, reprocessed.Status);
        Assert.Equal(2, reprocessed.Attempts);
        Assert.Null(reprocessed.Error);
        Assert.Equal(sale.Id, await WithDbAsync(db => db.Payments.AsNoTracking().Where(p => p.PeyTransactionId == transactionId).Select(p => p.SaleId).SingleAsync()));
        var audit = Assert.Single(await WithDbAsync(db => db.AuditLogs.AsNoTracking().ToListAsync()));
        Assert.Equal(AuditAction.WebhookReprocessed, audit.Action);
        Assert.Equal(failed.Id, audit.WebhookEventId);
        Assert.Equal(AdminEmail, audit.ActorEmail);
        Assert.Equal("Venta registrada después", audit.Note);
        Assert.Contains("Fallido → Procesado", audit.Summary);

        var after = WebUtility.HtmlDecode(await GetHtmlAsync(admin, detailUrl));
        Assert.Contains("data-webhook-notice", after);
        Assert.Contains("Fallido → Procesado", after);
        Assert.Contains("data-webhook-status=\"Processed\"", after);
        Assert.DoesNotContain("data-webhook-reprocess", after);
        Assert.Contains($"href=\"/admin/transacciones/ventas/{sale.Id}\"", after); // relacionado con la venta
    }

    [Fact]
    public async Task Un_ignorado_se_puede_reprocesar_y_sigue_ignorado()
    {
        var body = QrPaymentBody(NewTransactionId(), "IDCFIJO0000000000001", 10m,
            new Dictionary<string, string> { [SaleMetadata.KindKey] = SaleMetadata.StaticKind });
        Assert.Equal("Ignored", await WebhookStatusAsync(await PostWebhookAsync(body)));
        var ignored = await SingleWebhookAsync();
        var admin = await AdminAsync();
        var detailUrl = $"{Url}/{ignored.Id}";

        var response = await PostFormAsync(admin, detailUrl, new Dictionary<string, string> { ["Confirm.Note"] = "Revisión" }, $"{detailUrl}/reprocesar");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var after = await LoadAsync(ignored.Id);
        Assert.Equal(WebhookEventStatus.Ignored, after.Status);
        Assert.Equal(2, after.Attempts);
        Assert.Contains("quedó ignorado", WebUtility.HtmlDecode(await GetHtmlAsync(admin, detailUrl)));
        Assert.Single(await WithDbAsync(db => db.AuditLogs.AsNoTracking().ToListAsync()));
    }

    [Fact]
    public async Task Uno_procesado_no_se_reprocesa()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);
        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(QrPaymentBodyFor(sale))));
        var processed = await SingleWebhookAsync();
        var admin = await AdminAsync();
        var detailUrl = $"{Url}/{processed.Id}";

        var html = await GetHtmlAsync(admin, detailUrl);
        var response = await PostFormAsync(admin, detailUrl, new Dictionary<string, string> { ["Confirm.Note"] = "Otra vez" }, $"{detailUrl}/reprocesar");

        Assert.DoesNotContain("data-webhook-reprocess", html);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(1, (await LoadAsync(processed.Id)).Attempts);
        Assert.Empty(await WithDbAsync(db => db.AuditLogs.AsNoTracking().ToListAsync()));
        Assert.Contains("ya está procesado", WebUtility.HtmlDecode(await GetHtmlAsync(admin, detailUrl)));
    }

    [Fact]
    public async Task Sin_motivo_no_se_reprocesa()
    {
        var failed = await InsertWebhookEventAsync(PeyWebhookEventTypes.CodiQrPaymentReceived, "{}", DateTimeOffset.UtcNow, WebhookEventStatus.Failed);
        var admin = await AdminAsync();
        var detailUrl = $"{Url}/{failed.Id}";

        var html = await GetHtmlAsync(admin, detailUrl);
        var response = await PostFormAsync(admin, detailUrl, new Dictionary<string, string> { ["Confirm.Note"] = "  " }, $"{detailUrl}/reprocesar");

        Assert.Contains("data-val-required=\"Escribe el motivo.\"", html); // validación en el cliente
        Assert.Contains("__RequestVerificationToken", html);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(1, (await LoadAsync(failed.Id)).Attempts);
        Assert.Empty(await WithDbAsync(db => db.AuditLogs.AsNoTracking().ToListAsync()));
        Assert.Contains("Escribe el motivo.", WebUtility.HtmlDecode(await GetHtmlAsync(admin, detailUrl)));
    }

    [Fact]
    public async Task Sin_token_antiforgery_no_se_reprocesa()
    {
        var failed = await InsertWebhookEventAsync(PeyWebhookEventTypes.CodiQrPaymentReceived, "{}", DateTimeOffset.UtcNow, WebhookEventStatus.Failed);
        var admin = await AdminAsync();

        var response = await admin.PostAsync($"{Url}/{failed.Id}/reprocesar",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["Confirm.Note"] = "Sin token" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, (await LoadAsync(failed.Id)).Attempts);
    }

    [Fact]
    public async Task El_payload_se_muestra_con_la_clabe_enmascarada()
    {
        var body = QrPaymentBody(NewTransactionId(), "IDCSINVENTA000000001", 50m, external: null)
            .Replace("000000000000000000", TestClabe, StringComparison.Ordinal);
        await PostWebhookAsync(body);
        var webhook = await SingleWebhookAsync();
        var admin = await AdminAsync();

        var html = WebUtility.HtmlDecode(await GetHtmlAsync(admin, $"{Url}/{webhook.Id}"));
        var list = await GetHtmlAsync(admin, Url);

        Assert.Contains(TestClabe, webhook.Payload); // en la base sigue crudo
        Assert.DoesNotContain(TestClabe, html);
        Assert.Contains(Clabe.Mask(TestClabe), html);
        Assert.Contains("\"codiQrId\": \"IDCSINVENTA000000001\"", html);
        Assert.Contains("data-webhook-payload", html);
        Assert.DoesNotContain(TestClabe, list);
        Assert.Matches(new Regex("class=\"nav-link active\" href=\"/admin/webhooks\""), html);
    }

    [Fact]
    public async Task Filtros_de_estatus_tipo_atorados_y_busqueda()
    {
        var now = DateTimeOffset.UtcNow;
        var failed = await InsertWebhookEventAsync(PeyWebhookEventTypes.CodiQrPaymentReceived, "{\"transactionId\":\"TXBUSCADO123\"}", now.AddHours(-1), WebhookEventStatus.Failed);
        var stuck = await InsertWebhookEventAsync(PeyWebhookEventTypes.CodiPnPaymentReceived, "{}", now.AddMinutes(-30), WebhookEventStatus.Received);
        var recent = await InsertWebhookEventAsync(PeyWebhookEventTypes.CodiPnPaymentReceived, "{}", now.AddMinutes(-1), WebhookEventStatus.Received);
        var bank = await InsertWebhookEventAsync(PeyWebhookEventTypes.BankAccountUpdated, "{}", now.AddHours(-2), WebhookEventStatus.Processed);
        var unknown = await InsertWebhookEventAsync("otro.evento", "{}", now.AddHours(-3), WebhookEventStatus.Failed);
        var admin = await AdminAsync();

        var all = await GetHtmlAsync(admin, Url);

        Assert.Equal([recent.Id, stuck.Id, failed.Id, bank.Id, unknown.Id], RowIds(all));
        Assert.Contains("data-webhook-failed-count=\"2\"", all);
        Assert.Contains("data-webhook-stuck-count=\"1\"", all);
        Assert.Contains($"data-webhook-row=\"{stuck.Id}\" data-status=\"Received\" data-stuck=\"true\"", all);
        Assert.Equal([failed.Id, unknown.Id], RowIds(await GetHtmlAsync(admin, Url + "?estatus=fallidos")));
        Assert.Equal([stuck.Id], RowIds(await GetHtmlAsync(admin, Url + "?estatus=atorados")));
        Assert.Equal([recent.Id, stuck.Id], RowIds(await GetHtmlAsync(admin, Url + "?estatus=recibidos")));
        Assert.Equal([bank.Id], RowIds(await GetHtmlAsync(admin, Url + "?tipo=cuenta")));
        Assert.Equal([unknown.Id], RowIds(await GetHtmlAsync(admin, Url + "?tipo=otro")));
        Assert.Equal([failed.Id], RowIds(await GetHtmlAsync(admin, Url + "?q=TXBUSCADO123")));
        Assert.Equal([stuck.Id], RowIds(await GetHtmlAsync(admin, Url + "?q=" + stuck.WebhookId)));

        var shortQuery = await GetHtmlAsync(admin, Url + "?q=TXB");
        Assert.Empty(RowIds(shortQuery));
        Assert.Contains("data-webhook-query-short", shortQuery);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Url}/999999")).StatusCode);
    }

    private Task<WebhookEvent> LoadAsync(int id) => WithDbAsync(db => db.WebhookEvents.AsNoTracking().SingleAsync(e => e.Id == id));

    private Task<WebhookEvent> SingleWebhookAsync() => WithDbAsync(db => db.WebhookEvents.AsNoTracking().SingleAsync());

    private static List<int> RowIds(string html) =>
        RowRegex().Matches(html).Select(m => int.Parse(m.Groups[1].Value)).ToList();

    [GeneratedRegex("data-webhook-row=\"(\\d+)\"")]
    private static partial Regex RowRegex();
}
