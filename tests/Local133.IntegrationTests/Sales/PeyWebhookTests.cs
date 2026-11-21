using System.Net;
using System.Text.Json;
using Local133.Application.BankAccounts;
using Local133.Application.Sales;
using Local133.Domain.BankAccounts;
using Local133.Domain.Merchants;
using Local133.Domain.Pey;
using Local133.Domain.Sales;
using Local133.Domain.Settings;
using Local133.Domain.Webhooks;
using Local133.Infrastructure.Webhooks;
using Local133.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Local133.IntegrationTests.Sales;

/// <summary>
/// Pasos 2.3 y 2.5: <c>POST /api/webhooks/pey</c> (firma Standard Webhooks, bitácora, idempotencia) y lo que procesa:
/// pago de un QR (Payment, venta pagada, aviso en tiempo real) y activación de la cuenta bancaria.
/// </summary>
public class PeyWebhookTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    // --- Firma y bitácora -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Pago_con_firma_valida_crea_el_Payment_marca_la_venta_pagada_y_avisa_al_vendedor()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client, "150.00");
        var paidAt = DateTimeOffset.UtcNow.AddSeconds(-20);
        var transactionId = NewTransactionId();
        var webhookId = NewWebhookId();

        var response = await PostWebhookAsync(QrPaymentBodyFor(sale, transactionId, paidAt: paidAt), webhookId);

        Assert.Equal("Processed", await WebhookStatusAsync(response));
        var payment = Assert.Single(await WithDbAsync(db => db.Payments.AsNoTracking().ToListAsync()));
        Assert.Equal(sale.Id, payment.SaleId);
        Assert.Equal(sale.MerchantId, payment.MerchantId);
        Assert.Equal(transactionId, payment.PeyTransactionId);
        Assert.Equal(150m, payment.Amount);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(paidAt.ToUnixTimeSeconds()), payment.PaidAt);
        Assert.Equal("0192191042", payment.TrackingKey);
        Assert.Equal(40012, payment.PayerBankCode);

        var paid = await LoadSaleAsync(sale.Id);
        Assert.Equal(SaleStatus.Paid, paid.Status);
        Assert.Equal(payment.PaidAt, paid.PaidAt);

        var webhook = Assert.Single(await WebhookEventsAsync());
        Assert.Equal(webhookId, webhook.WebhookId);
        Assert.Equal(PeyWebhookEventTypes.CodiQrPaymentReceived, webhook.EventType);
        Assert.Equal(WebhookEventStatus.Processed, webhook.Status);
        Assert.Equal(1, webhook.Attempts);
        Assert.NotNull(webhook.ProcessedAt);
        Assert.Contains(transactionId, webhook.Payload);

        Assert.Equal((sale.MerchantId, sale.Id), Assert.Single(Factory.Notifier.Notifications));
    }

    [Fact]
    public async Task Firma_invalida_responde_401_y_no_guarda_nada()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);

        var response = await PostWebhookAsync(QrPaymentBodyFor(sale), secret: "whsec_" + Convert.ToBase64String(new byte[24]));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        await AssertNothingRecordedAsync(sale.Id);
    }

    [Fact]
    public async Task Body_alterado_despues_de_firmar_responde_401()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);
        var body = QrPaymentBodyFor(sale);
        var id = NewWebhookId();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookUrl)
        {
            Content = new StringContent(body.Replace("150", "1"), System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(StandardWebhookVerifier.IdHeader, id);
        request.Headers.Add(StandardWebhookVerifier.TimestampHeader, timestamp);
        request.Headers.Add(StandardWebhookVerifier.SignatureHeader,
            StandardWebhookVerifier.ComputeSignature(Local133WebFactory.WebhookSecret, id, timestamp, body));

        var response = await CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNothingRecordedAsync(sale.Id);
    }

    [Fact]
    public async Task Sin_headers_de_firma_responde_401()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);

        var response = await CreateClient().PostAsync(WebhookUrl,
            new StringContent(QrPaymentBodyFor(sale), System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNothingRecordedAsync(sale.Id);
    }

    [Fact]
    public async Task Timestamp_de_hace_mas_de_5_minutos_responde_401()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);

        var response = await PostWebhookAsync(QrPaymentBodyFor(sale), sentAt: DateTimeOffset.UtcNow.AddMinutes(-6));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNothingRecordedAsync(sale.Id);
    }

    [Fact]
    public async Task Sin_secreto_configurado_responde_500_para_que_Pey_reintente()
    {
        using var noSecret = Factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Pey:WebhookSecret"] = "" })));
        var client = noSecret.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

        var response = await PostWebhookAsync("{\"type\":\"codi.qr.payment_received\"}", client: client);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(await WebhookEventsAsync());
    }

    [Fact]
    public async Task El_mismo_webhook_id_repetido_responde_200_sin_procesar_de_nuevo()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);
        var body = QrPaymentBodyFor(sale);
        var webhookId = NewWebhookId();

        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(body, webhookId)));
        Assert.Equal("Duplicate", await WebhookStatusAsync(await PostWebhookAsync(body, webhookId)));

        Assert.Single(await WithDbAsync(db => db.Payments.AsNoTracking().ToListAsync()));
        var webhook = Assert.Single(await WebhookEventsAsync());
        Assert.Equal(1, webhook.Attempts);
        Assert.Single(Factory.Notifier.Notifications);
    }

    [Fact]
    public async Task La_misma_transaccion_con_otro_webhook_id_no_duplica_el_pago()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);
        var body = QrPaymentBodyFor(sale, NewTransactionId());

        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(body)));
        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(body)));

        Assert.Single(await WithDbAsync(db => db.Payments.AsNoTracking().ToListAsync()));
        Assert.All(await WebhookEventsAsync(), e => Assert.Equal(WebhookEventStatus.Processed, e.Status));
        Assert.Equal(2, (await WebhookEventsAsync()).Count);
        Assert.Single(Factory.Notifier.Notifications);
    }

    [Fact]
    public async Task Un_webhook_fallido_se_vuelve_a_procesar_cuando_Pey_lo_reintenta()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);
        var body = QrPaymentBody(NewTransactionId(), "IDCQUEAUNNOEXISTE", sale.Amount, external: null);
        var webhookId = NewWebhookId();

        Assert.Equal("Failed", await WebhookStatusAsync(await PostWebhookAsync(body, webhookId)));
        // La venta aparece después con ese IDC (p. ej. el QR se guardó tarde).
        await UpdateSaleAsync(sale.Id, s => s.PeyQrId = "IDCQUEAUNNOEXISTE");

        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(body, webhookId)));

        var webhook = Assert.Single(await WebhookEventsAsync());
        Assert.Equal(WebhookEventStatus.Processed, webhook.Status);
        Assert.Equal(2, webhook.Attempts);
        Assert.Null(webhook.Error);
        Assert.Equal(SaleStatus.Paid, (await LoadSaleAsync(sale.Id)).Status);
    }

    // --- Pago de QR -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Un_centavo_se_registra()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client, "0.01");

        await PostWebhookAsync(QrPaymentBodyFor(sale));

        var payment = Assert.Single(await WithDbAsync(db => db.Payments.AsNoTracking().ToListAsync()));
        Assert.Equal(0.01m, payment.Amount);
        Assert.Equal(SaleStatus.Paid, (await LoadSaleAsync(sale.Id)).Status);
    }

    [Theory]
    [InlineData(SaleStatus.Expired)]
    [InlineData(SaleStatus.Cancelled)]
    public async Task Un_pago_tardio_deja_pagada_una_venta_vencida_o_cancelada(SaleStatus status)
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);
        await UpdateSaleAsync(sale.Id, s => s.Status = status);

        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(QrPaymentBodyFor(sale))));

        Assert.Equal(SaleStatus.Paid, (await LoadSaleAsync(sale.Id)).Status);
        Assert.Single(Factory.Notifier.Notifications);
    }

    [Fact]
    public async Task Un_segundo_pago_del_mismo_QR_se_registra_y_la_venta_sigue_pagada()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);
        var first = DateTimeOffset.UtcNow.AddMinutes(-2);

        await PostWebhookAsync(QrPaymentBodyFor(sale, paidAt: first));
        await PostWebhookAsync(QrPaymentBodyFor(sale));

        Assert.Equal(2, (await WithDbAsync(db => db.Payments.AsNoTracking().ToListAsync())).Count);
        var paid = await LoadSaleAsync(sale.Id);
        Assert.Equal(SaleStatus.Paid, paid.Status);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(first.ToUnixTimeSeconds()), paid.PaidAt);
    }

    [Fact]
    public async Task Sin_codiQrId_se_encuentra_la_venta_por_la_metadata()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);

        var response = await PostWebhookAsync(QrPaymentBody(NewTransactionId(), codiQrId: null, sale.Amount, SaleMetadataFor(sale)));

        Assert.Equal("Processed", await WebhookStatusAsync(response));
        Assert.Equal(SaleStatus.Paid, (await LoadSaleAsync(sale.Id)).Status);
    }

    [Fact]
    public async Task Metadata_de_otro_vendedor_no_se_concilia()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);
        var metadata = SaleMetadataFor(sale);
        metadata[SaleMetadata.MerchantIdKey] = (sale.MerchantId + 1000).ToString();

        var response = await PostWebhookAsync(QrPaymentBody(NewTransactionId(), sale.PeyQrId, sale.Amount, metadata));

        Assert.Equal("Failed", await WebhookStatusAsync(response));
        await AssertNoPaymentAsync(sale.Id);
        var webhook = Assert.Single(await WebhookEventsAsync());
        Assert.Contains("vendedor", webhook.Error);
    }

    [Fact]
    public async Task QR_desconocido_queda_fallido_para_revision_y_responde_200()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);

        var response = await PostWebhookAsync(QrPaymentBody(NewTransactionId(), "IDCDESCONOCIDO", 10m, external: null));

        Assert.Equal("Failed", await WebhookStatusAsync(response));
        await AssertNoPaymentAsync(sale.Id);
        var webhook = Assert.Single(await WebhookEventsAsync());
        Assert.Equal(WebhookEventStatus.Failed, webhook.Status);
        Assert.Contains("IDCDESCONOCIDO", webhook.Error);
        Assert.Empty(Factory.Notifier.Notifications);
    }

    [Fact]
    public async Task Pagos_del_QR_fijo_se_ignoran_por_ahora()
    {
        var response = await PostWebhookAsync(QrPaymentBody(NewTransactionId(), "IDCFIJO", 58m,
            new Dictionary<string, string> { [SaleMetadata.KindKey] = SaleMetadata.StaticKind }));

        Assert.Equal("Ignored", await WebhookStatusAsync(response));
        Assert.Empty(await WithDbAsync(db => db.Payments.AsNoTracking().ToListAsync()));
        Assert.Equal(WebhookEventStatus.Ignored, Assert.Single(await WebhookEventsAsync()).Status);
    }

    [Theory]
    [InlineData("{\"type\":\"codi.pn.payment_received\",\"transactionId\":\"01TX\",\"amount\":10}", "Failed")] // sin venta
    [InlineData("{\"type\":\"codi.pn.payment_received\",\"amount\":10}", "Failed")]
    [InlineData("{\"type\":\"codi.pn.payment_received\",\"transactionId\":\"01TX\"}", "Failed")]
    [InlineData("{\"type\":\"payment_link.status_updated\"}", "Failed")] // sin paymentLinkId
    [InlineData("{\"type\":\"payment_link.status_updated\",\"paymentLinkId\":\"01PL\",\"status\":\"rara\"}", "Failed")]
    [InlineData("{\"type\":\"payment_link.status_updated\",\"paymentLinkId\":\"01PL\",\"status\":\"paid\"}", "Failed")] // sin cargo
    [InlineData("{\"type\":\"algo.nuevo\"}", "Failed")]
    [InlineData("{\"amount\":10}", "Failed")]
    [InlineData("esto no es json", "Failed")]
    [InlineData("{\"type\":\"codi.qr.payment_received\",\"amount\":10}", "Failed")]
    [InlineData("{\"type\":\"codi.qr.payment_received\",\"transactionId\":\"01TX\"}", "Failed")]
    [InlineData("{\"type\":\"codi.qr.payment_received\",\"transactionId\":\"01TX\",\"amount\":\"diez\"}", "Failed")]
    public async Task Eventos_sin_manejador_o_mal_formados_responden_200_y_quedan_en_la_bitacora(string body, string expected)
    {
        var response = await PostWebhookAsync(body);

        Assert.Equal(expected, await WebhookStatusAsync(response));
        var webhook = Assert.Single(await WebhookEventsAsync());
        Assert.Equal(body, webhook.Payload);
        Assert.NotNull(webhook.Error);
    }

    // --- bank.account.updated ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Cuenta_activada_por_webhook_activa_al_vendedor_sin_contar_como_consulta()
    {
        await CreateMerchantAsync(Email);
        var peyId = "01JBAVWE4NS5TGXHWGMX3KH23Q";
        DateTimeOffset? checkedAt = null;
        await WithDbAsync(async db =>
        {
            var merchant = await MerchantOfAsync(db, Email);
            merchant.Status = MerchantStatus.BankAccountInReview;
            var account = BankAccount.Submit(merchant.Id, "Ramón Orraca Lascurain", TestClabe, "Puesto", PeyEntity.Person,
                peyId, PeyCodiStatus.Processing, DateTimeOffset.UtcNow.AddDays(-1));
            db.BankAccounts.Add(account);
            await db.SaveChangesAsync();
            checkedAt = account.StatusCheckedAt;
        });

        var response = await PostWebhookAsync(BankAccountUpdatedBody(peyId));

        Assert.Equal("Processed", await WebhookStatusAsync(response));
        var (account, merchant) = await WithDbAsync(async db =>
        {
            var m = await MerchantOfAsync(db, Email);
            return (await db.BankAccounts.AsNoTracking().SingleAsync(b => b.MerchantId == m.Id), m);
        });
        Assert.Equal(PeyCodiStatus.Active, account.CodiStatus);
        Assert.Equal(MerchantStatus.Active, merchant.Status);
        Assert.Equal(checkedAt, account.StatusCheckedAt);
        Assert.Empty(Factory.Gateway.BankAccountLookups); // no consulta a Pey

        var sent = Assert.Single(ActivationEmails());
        Assert.Equal(Email, sent.To);
        Assert.Equal(ActivationEmailLink, sent.Link);
        Assert.Contains("•••• 5713", System.Net.WebUtility.HtmlDecode(sent.Html));
        Assert.DoesNotContain(TestClabe, sent.Html);
        Assert.NotNull(merchant.ActivationEmailSentAt);
    }

    [Fact]
    public async Task Activacion_repetida_no_cambia_nada()
    {
        await ActiveMerchantAsync(Email);
        var peyId = await WithDbAsync(db => db.BankAccounts.Select(b => b.PeyBankAccountId).SingleAsync());

        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(BankAccountUpdatedBody(peyId))));

        Assert.Equal(MerchantStatus.Active, (await WithDbAsync(db => MerchantOfAsync(db, Email))).Status);
        Assert.Empty(ActivationEmails()); // ya estaba activo: no hay transición ni correo
    }

    [Fact]
    public async Task La_misma_activacion_vista_varias_veces_manda_un_solo_correo()
    {
        await CreateMerchantAsync(Email);
        var peyId = "01JBAVWE4NS5TGXHWGMX3KH23Q";
        var merchantId = await WithDbAsync(async db =>
        {
            var merchant = await MerchantOfAsync(db, Email);
            merchant.Status = MerchantStatus.BankAccountInReview;
            db.BankAccounts.Add(BankAccount.Submit(merchant.Id, "Ramón Orraca Lascurain", TestClabe, "Puesto", PeyEntity.Person,
                peyId, PeyCodiStatus.Processing, DateTimeOffset.UtcNow.AddDays(-1)));
            await db.SaveChangesAsync();
            return merchant.Id;
        });

        await PostWebhookAsync(BankAccountUpdatedBody(peyId));
        await PostWebhookAsync(BankAccountUpdatedBody(peyId)); // otro webhook-id, sin transición
        // El sondeo o un segundo camino concurrente llaman al aviso otra vez: el candado lo frena.
        await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IBankAccountActivationNotifier>().NotifyActivatedAsync(merchantId);
        }));

        Assert.Single(ActivationEmails());
    }

    [Fact]
    public async Task Si_el_correo_falla_el_webhook_queda_procesado()
    {
        await CreateMerchantAsync(Email);
        var peyId = "01JBAVWE4NS5TGXHWGMX3KH23Q";
        await WithDbAsync(async db =>
        {
            var merchant = await MerchantOfAsync(db, Email);
            merchant.Status = MerchantStatus.BankAccountInReview;
            db.BankAccounts.Add(BankAccount.Submit(merchant.Id, "Ramón Orraca Lascurain", TestClabe, "Puesto", PeyEntity.Person,
                peyId, PeyCodiStatus.Processing, DateTimeOffset.UtcNow.AddDays(-1)));
            await db.SaveChangesAsync();
        });
        Factory.Emails.FailWith = new InvalidOperationException("Resend caído");

        var response = await PostWebhookAsync(BankAccountUpdatedBody(peyId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Processed", await WebhookStatusAsync(response));
        Assert.Equal(MerchantStatus.Active, (await WithDbAsync(db => MerchantOfAsync(db, Email))).Status);
    }

    [Fact]
    public async Task Activacion_de_una_cuenta_desconocida_queda_fallida()
    {
        var response = await PostWebhookAsync(BankAccountUpdatedBody("01CUENTAQUENOEXISTE0000000"));

        Assert.Equal("Failed", await WebhookStatusAsync(response));
        Assert.Contains("01CUENTAQUENOEXISTE0000000", Assert.Single(await WebhookEventsAsync()).Error);
    }

    // --- Ayudas -----------------------------------------------------------------------------------------------------

    private static string BankAccountUpdatedBody(string peyBankAccountId) => JsonSerializer.Serialize(new
    {
        type = PeyWebhookEventTypes.BankAccountUpdated,
        bankAccountId = peyBankAccountId,
        name = "Ramón Orraca Lascurain",
        ownerType = "API_KEY",
        ownerId = "01M3QMMR6X9CBVDY1SF3G8QWNW",
        codiStatus = "ACTIVE",
        clabe = TestClabe,
        entity = "PERSON",
    });

    private Task<List<WebhookEvent>> WebhookEventsAsync() =>
        WithDbAsync(db => db.WebhookEvents.AsNoTracking().OrderBy(e => e.Id).ToListAsync());

    private async Task AssertNothingRecordedAsync(int saleId)
    {
        Assert.Empty(await WebhookEventsAsync());
        await AssertNoPaymentAsync(saleId);
    }

    private async Task AssertNoPaymentAsync(int saleId)
    {
        Assert.Empty(await WithDbAsync(db => db.Payments.AsNoTracking().ToListAsync()));
        Assert.Equal(SaleStatus.Pending, (await LoadSaleAsync(saleId)).Status);
    }

}
