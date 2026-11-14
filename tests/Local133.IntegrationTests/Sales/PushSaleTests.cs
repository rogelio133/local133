using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Local133.Application.Payments;
using Local133.Application.Sales;
using Local133.Domain.Merchants;
using Local133.Domain.Pey;
using Local133.Domain.Sales;
using Local133.Domain.Settings;
using Local133.Domain.Webhooks;
using Local133.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Local133.IntegrationTests.Sales;

/// <summary>
/// Paso 2.6: cobro por celular (<c>POST /codi/pn</c>) desde el mismo formulario de /app/cobrar, vista de la venta sin
/// QR y pago por el webhook <c>codi.pn.payment_received</c> (se encuentra por el <c>transactionId</c>).
/// </summary>
public partial class PushSaleTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    private const string Url = "/app/cobrar";

    // --- Formulario -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task El_formulario_ofrece_QR_o_celular_con_QR_por_defecto()
    {
        var client = await ActiveMerchantAsync(Email);

        var html = await GetHtmlAsync(client, Url);

        Assert.Contains("data-charge-method", html);
        Assert.Matches(new Regex("id=\"charge-method-qr\"[^>]*value=\"DynamicQr\"[^>]*checked=\"checked\""), html);
        Assert.Matches(new Regex("id=\"charge-method-phone\"[^>]*value=\"PushNotification\""), html);
        Assert.DoesNotMatch(new Regex("id=\"charge-method-phone\"[^>]*checked"), html);
        Assert.Contains("name=\"Input.PhoneNumber\"", html);
        Assert.Contains("type=\"tel\"", html);
        Assert.Contains("inputmode=\"numeric\"", html);
        Assert.Contains($"data-val-required=\"{SalePhoneMessages.Required}\"", html);
        Assert.Contains($"data-val-phone=\"{SalePhoneMessages.Invalid}\"", html);
        Assert.Contains("<div class=\"d-none\" data-phone-field>", html); // oculto con QR
        Assert.Contains("disabled=\"disabled\"", PhoneInputTag(html));
        Assert.Contains("Tu cliente debe tener CoDi activo", html);
        Assert.Contains("data-charge-submit-label>Generar QR<", html);
        Assert.Contains("/js/charge-form", html);
    }

    [Fact]
    public async Task Cobro_al_celular_envia_el_push_a_Pey_y_muestra_la_venta_sin_QR()
    {
        var client = await ActiveMerchantAsync(Email);

        var response = await PostPushChargeAsync(client, "+52 (55) 1234-5678", "150.50");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var sale = Assert.Single(await LoadSalesAsync());
        Assert.Equal(SaleUrl(sale.Id), Location(response));
        Assert.Empty(Factory.Gateway.CodiQrRequests);

        var sent = Assert.Single(Factory.Gateway.PushRequests);
        Assert.Equal(150.50m, sent.Amount);
        Assert.Equal("5512345678", sent.PhoneNumber);
        Assert.Equal(SystemSettings.DefaultSaleQrExpirationSeconds, sent.ExpirationSeconds);
        Assert.Equal(1, sent.NumRef);
        Assert.Equal("Tacos Don Ramón · 2 tacos al pastor", sent.Concept);
        Assert.Equal(await PeyBankAccountIdAsync(), sent.BankAccountId);
        Assert.NotNull(sent.Metadata);
        Assert.Equal(SaleMetadata.SaleKind, sent.Metadata[SaleMetadata.KindKey]);
        Assert.Equal(sale.MerchantId.ToString(), sent.Metadata[SaleMetadata.MerchantIdKey]);
        Assert.Equal(sale.Id.ToString(), sent.Metadata[SaleMetadata.SaleIdKey]);

        Assert.Equal(ChargeMethod.PushNotification, sale.Method);
        Assert.Equal(SaleSource.DirectAmount, sale.Source);
        Assert.Equal(SaleStatus.Pending, sale.Status);
        Assert.Equal("5512345678", sale.PhoneNumber);
        Assert.StartsWith("01FAKEPN", sale.PeyTransactionId);
        Assert.Equal(20, sale.PeyCodiId!.Length);
        Assert.Null(sale.PeyQrId);
        Assert.Null(sale.QrString);
        Assert.NotNull(sale.ExpiresAt);
        Assert.InRange((sale.ExpiresAt.Value - sale.CreatedAt).TotalSeconds, 599.9, 600.1);

        var html = await GetHtmlAsync(client, SaleUrl(sale.Id));
        Assert.Contains("data-sale-status=\"Pending\"", html);
        Assert.Contains("data-sale-push", html);
        Assert.Contains("data-sale-phone>•••• 5678<", html);
        Assert.DoesNotContain("5512345678", html);
        Assert.DoesNotContain("data-sale-qr", html);
        Assert.DoesNotContain("/lib/qrcodejs/qrcode.min", html);
        Assert.Contains("/js/sale-qr", html); // espera el pago igual que el QR
        Assert.Matches(new Regex("data-expires-in=\"(59\\d|600)\""), html);
        Assert.Contains("data-cancel-sale", html);
        Assert.Contains("CoDi no permite anular la solicitud", html);
        Assert.Matches(new Regex("class=\"nav-link active\" href=\"/app/cobrar\""), html);
    }

    [Theory]
    [InlineData("", SalePhoneMessages.Required)]
    [InlineData("   ", SalePhoneMessages.Required)]
    [InlineData("551234567", SalePhoneMessages.Invalid)]
    [InlineData("55123456789", SalePhoneMessages.Invalid)]
    [InlineData("55-1234-567a", SalePhoneMessages.Invalid)]
    [InlineData("52 55 1234 5678", SalePhoneMessages.Invalid)]
    public async Task Celular_faltante_o_invalido_muestra_el_error_sin_llamar_a_Pey(string phone, string message)
    {
        var client = await ActiveMerchantAsync(Email);

        var response = await PostPushChargeAsync(client, phone);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains(message, html);
        Assert.Matches(new Regex("id=\"charge-method-phone\"[^>]*checked=\"checked\""), html); // conserva el método
        Assert.Contains("<div data-phone-field>", html); // visible
        Assert.DoesNotContain("disabled", PhoneInputTag(html));
        Assert.Contains("data-charge-submit-label>Enviar cobro<", html);
        Assert.Empty(Factory.Gateway.PushRequests);
        Assert.Empty(Factory.Gateway.CodiQrRequests);
        Assert.Empty(await LoadSalesAsync());
    }

    [Fact]
    public async Task Con_QR_el_celular_no_se_pide_ni_se_valida()
    {
        var client = await ActiveMerchantAsync(Email);

        var response = await PostFormAsync(client, Url, new Dictionary<string, string>
        {
            ["Input.Method"] = nameof(ChargeMethod.DynamicQr),
            ["Input.Amount"] = "10",
            ["Input.Concept"] = "Agua",
            ["Input.PhoneNumber"] = "no es un celular",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Single(Factory.Gateway.CodiQrRequests);
        Assert.Empty(Factory.Gateway.PushRequests);
        var sale = Assert.Single(await LoadSalesAsync());
        Assert.Equal(ChargeMethod.DynamicQr, sale.Method);
        Assert.Null(sale.PhoneNumber);
    }

    [Theory]
    [InlineData(nameof(ChargeMethod.StaticQr))]
    [InlineData("Tarjeta")]
    public async Task Un_metodo_que_no_es_QR_ni_celular_se_rechaza(string method)
    {
        var client = await ActiveMerchantAsync(Email);

        var response = await PostFormAsync(client, Url, new Dictionary<string, string>
        {
            ["Input.Method"] = method,
            ["Input.Amount"] = "10",
            ["Input.Concept"] = "Agua",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(Factory.Gateway.CodiQrRequests);
        Assert.Empty(Factory.Gateway.PushRequests);
        Assert.Empty(await LoadSalesAsync());
    }

    [Fact]
    public async Task Vendedor_que_no_puede_cobrar_tampoco_cobra_al_celular()
    {
        var client = await ActiveMerchantAsync(Email, status: MerchantStatus.Suspended);

        var response = await PostFormAsync(client, TokenPage, new Dictionary<string, string>
        {
            ["Input.Method"] = nameof(ChargeMethod.PushNotification),
            ["Input.Amount"] = "10",
            ["Input.Concept"] = "Agua",
            ["Input.PhoneNumber"] = "5512345678",
        }, postUrl: Url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Url, Location(response));
        Assert.Empty(Factory.Gateway.PushRequests);
        Assert.Empty(await LoadSalesAsync());
    }

    // --- Fallas de Pey ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Pey_rechaza_el_push_muestra_el_aviso_y_no_guarda_la_venta()
    {
        Factory.Gateway.PushException = new PaymentGatewayValidationException(
            "celular inválido", PeyErrorType.InvalidParameter, 400, "req-1", "Invalid phoneNumber");
        var client = await ActiveMerchantAsync(Email);

        var response = await PostPushChargeAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Revisa el monto y el celular", html);
        Assert.Empty(await LoadSalesAsync());
    }

    [Fact]
    public async Task Falla_de_configuracion_no_guarda_la_venta()
    {
        Factory.Gateway.PushException = new InvalidOperationException("Falta Pey:ApiKey");
        var client = await ActiveMerchantAsync(Email);

        var response = await PostPushChargeAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("No pudimos enviar el cobro al celular", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Empty(await LoadSalesAsync());
    }

    [Fact]
    public async Task Sin_respuesta_de_Pey_la_venta_queda_cancelada_y_si_el_cliente_paga_se_registra()
    {
        Factory.Gateway.PushException = new PaymentGatewayUnavailableException("timeout");
        var client = await ActiveMerchantAsync(Email);

        var response = await PostPushChargeAsync(client);

        // Se conserva la venta: el push pudo haberle llegado al cliente.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var sale = Assert.Single(await LoadSalesAsync());
        Assert.Equal(SaleUrl(sale.Id), Location(response));
        Assert.Equal(SaleStatus.Cancelled, sale.Status);
        Assert.NotNull(sale.CancelledAt);
        Assert.Null(sale.PeyTransactionId);

        var html = await GetHtmlAsync(client, SaleUrl(sale.Id));
        Assert.Contains("data-push-unconfirmed", html);
        Assert.DoesNotContain("data-cancel-sale", html);
        Assert.Contains("/js/sale-qr", html); // sigue escuchando por si llega el pago

        // El cliente sí recibió la solicitud y pagó: el webhook la encuentra por la metadata.
        var transactionId = NewTransactionId();
        var webhook = await PostWebhookAsync(PushPaymentBodyFor(sale, transactionId));

        Assert.Equal("Processed", await WebhookStatusAsync(webhook));
        var paid = await LoadSaleAsync(sale.Id);
        Assert.Equal(SaleStatus.Paid, paid.Status);
        Assert.Equal(transactionId, paid.PeyTransactionId);
        Assert.Equal(transactionId, Assert.Single(await PaymentsAsync()).PeyTransactionId);

        // Un siguiente cobro no se salta el folio ni choca.
        Factory.Gateway.PushException = null;
        Assert.Equal(2, (await ChargeByPhoneAsync(client)).Folio);
    }

    // --- Webhook codi.pn.payment_received ---------------------------------------------------------------------------

    [Fact]
    public async Task Webhook_de_push_registra_el_pago_marca_la_venta_pagada_y_avisa()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeByPhoneAsync(client, amount: "150.00");
        var paidAt = DateTimeOffset.UtcNow.AddSeconds(-15);
        var webhookId = NewWebhookId();

        var response = await PostWebhookAsync(PushPaymentBodyFor(sale, paidAt: paidAt), webhookId);

        Assert.Equal("Processed", await WebhookStatusAsync(response));
        var payment = Assert.Single(await PaymentsAsync());
        Assert.Equal(sale.Id, payment.SaleId);
        Assert.Equal(sale.MerchantId, payment.MerchantId);
        Assert.Equal(sale.PeyTransactionId, payment.PeyTransactionId);
        Assert.Equal(150m, payment.Amount);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(paidAt.ToUnixTimeSeconds()), payment.PaidAt);
        Assert.Equal("0192191043", payment.TrackingKey);
        Assert.Equal(40012, payment.PayerBankCode);
        Assert.Equal(SystemSettings.DefaultCommissionRate, payment.CommissionRate);
        Assert.Equal(4.50m, payment.CommissionAmount);

        Assert.Equal(SaleStatus.Paid, (await LoadSaleAsync(sale.Id)).Status);
        var webhook = Assert.Single(await WebhookEventsAsync());
        Assert.Equal(PeyWebhookEventTypes.CodiPnPaymentReceived, webhook.EventType);
        Assert.Equal(WebhookEventStatus.Processed, webhook.Status);
        Assert.Equal((sale.MerchantId, sale.Id), Assert.Single(Factory.Notifier.Notifications));

        using var api = JsonDocument.Parse(await GetJsonAsync(client, $"/api/sales/{sale.Id}"));
        Assert.Equal("Paid", api.RootElement.GetProperty("status").GetString());

        var html = await GetHtmlAsync(client, SaleUrl(sale.Id));
        Assert.Contains("data-sale-status=\"Paid\"", html);
        Assert.DoesNotContain("data-sale-push", html);
    }

    [Fact]
    public async Task Webhook_sin_metadata_se_encuentra_por_la_transaccion()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeByPhoneAsync(client);

        var response = await PostWebhookAsync(PushPaymentBody(sale.PeyTransactionId!, sale.Amount, external: null));

        Assert.Equal("Processed", await WebhookStatusAsync(response));
        Assert.Equal(sale.Id, Assert.Single(await PaymentsAsync()).SaleId);
    }

    [Fact]
    public async Task Webhook_repetido_o_reentregado_no_duplica_el_pago()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeByPhoneAsync(client);
        var body = PushPaymentBodyFor(sale);
        var webhookId = NewWebhookId();

        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(body, webhookId)));
        Assert.Equal("Duplicate", await WebhookStatusAsync(await PostWebhookAsync(body, webhookId)));
        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(body))); // otro webhook-id, misma transacción

        Assert.Single(await PaymentsAsync());
        Assert.Single(Factory.Notifier.Notifications);
    }

    [Fact]
    public async Task Pago_tardio_de_un_push_vencido_o_cancelado_tambien_cuenta()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeByPhoneAsync(client);
        await UpdateSaleAsync(sale.Id, s => s.Cancel(DateTimeOffset.UtcNow));

        var response = await PostWebhookAsync(PushPaymentBodyFor(sale));

        Assert.Equal("Processed", await WebhookStatusAsync(response));
        Assert.Equal(SaleStatus.Paid, (await LoadSaleAsync(sale.Id)).Status);
    }

    [Fact]
    public async Task Transaccion_desconocida_queda_fallida_con_200()
    {
        var response = await PostWebhookAsync(PushPaymentBody(NewTransactionId(), 10m, external: null));

        Assert.Equal("Failed", await WebhookStatusAsync(response));
        var webhook = Assert.Single(await WebhookEventsAsync());
        Assert.Equal(WebhookEventStatus.Failed, webhook.Status);
        Assert.Contains("No hay venta con la transacción", webhook.Error);
        Assert.Empty(await PaymentsAsync());
    }

    [Fact]
    public async Task Push_con_metadata_de_una_venta_con_otra_transaccion_no_se_registra()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeByPhoneAsync(client);

        var response = await PostWebhookAsync(PushPaymentBodyFor(sale, transactionId: NewTransactionId()));

        Assert.Equal("Failed", await WebhookStatusAsync(response));
        Assert.Contains("otra transacción", Assert.Single(await WebhookEventsAsync()).Error);
        Assert.Empty(await PaymentsAsync());
        Assert.Equal(SaleStatus.Pending, (await LoadSaleAsync(sale.Id)).Status);
    }

    [Fact]
    public async Task Un_push_no_paga_una_venta_con_QR_ni_un_QR_paga_un_cobro_por_celular()
    {
        var client = await ActiveMerchantAsync(Email);
        var qrSale = await ChargeAsync(client);
        var pushSale = await ChargeByPhoneAsync(client);

        var pushOnQr = await PostWebhookAsync(PushPaymentBody(NewTransactionId(), qrSale.Amount, SaleMetadataFor(qrSale)));
        var qrOnPush = await PostWebhookAsync(QrPaymentBody(NewTransactionId(), "IDCOTRO", pushSale.Amount, SaleMetadataFor(pushSale)));

        Assert.Equal("Failed", await WebhookStatusAsync(pushOnQr));
        Assert.Equal("Failed", await WebhookStatusAsync(qrOnPush));
        Assert.Empty(await PaymentsAsync());
        Assert.Equal(SaleStatus.Pending, (await LoadSaleAsync(qrSale.Id)).Status);
        Assert.Equal(SaleStatus.Pending, (await LoadSaleAsync(pushSale.Id)).Status);
    }

    [Fact]
    public async Task Metadata_de_otro_vendedor_no_se_registra()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeByPhoneAsync(client);
        var metadata = SaleMetadataFor(sale);
        metadata[SaleMetadata.MerchantIdKey] = (sale.MerchantId + 1000).ToString();

        var response = await PostWebhookAsync(PushPaymentBody(sale.PeyTransactionId!, sale.Amount, metadata));

        Assert.Equal("Failed", await WebhookStatusAsync(response));
        Assert.Empty(await PaymentsAsync());
    }

    // --- Aislamiento y cancelación ----------------------------------------------------------------------------------

    [Fact]
    public async Task La_venta_al_celular_de_otro_vendedor_da_404()
    {
        var owner = await ActiveMerchantAsync(Email);
        var sale = await ChargeByPhoneAsync(owner);
        var other = await ActiveMerchantAsync(OtherEmail);

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(SaleUrl(sale.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/sales/{sale.Id}")).StatusCode);
    }

    [Fact]
    public async Task El_vendedor_puede_cancelar_un_cobro_al_celular_pendiente()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeByPhoneAsync(client);

        var response = await PostFormAsync(client, SaleUrl(sale.Id), new Dictionary<string, string>(), postUrl: $"{SaleUrl(sale.Id)}/cancelar");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(SaleStatus.Cancelled, (await LoadSaleAsync(sale.Id)).Status);
        var html = await GetHtmlAsync(client, SaleUrl(sale.Id));
        Assert.Contains("data-cancelled-only", html);
        Assert.Contains("aceptar la solicitud", html);
        Assert.DoesNotContain("data-push-unconfirmed", html);
    }

    [Fact]
    public async Task Un_cobro_al_celular_vencido_muestra_que_la_solicitud_expiro()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeByPhoneAsync(client);
        await UpdateSaleAsync(sale.Id, s => s.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1));

        var html = await GetHtmlAsync(client, SaleUrl(sale.Id));

        Assert.Contains("data-expired=\"true\"", html);
        Assert.Contains("La solicitud expiró sin pago", html);
        Assert.DoesNotContain("data-sale-push", html);
        Assert.DoesNotContain("data-cancel-sale", html);
    }

    // --- Ayudas -----------------------------------------------------------------------------------------------------

    private Task<List<Sale>> LoadSalesAsync() =>
        WithDbAsync(db => db.Sales.AsNoTracking().OrderBy(s => s.Id).ToListAsync());

    private Task<List<Domain.Payments.Payment>> PaymentsAsync() =>
        WithDbAsync(db => db.Payments.AsNoTracking().ToListAsync());

    private Task<List<WebhookEvent>> WebhookEventsAsync() =>
        WithDbAsync(db => db.WebhookEvents.AsNoTracking().ToListAsync());

    private Task<string> PeyBankAccountIdAsync() =>
        WithDbAsync(async db =>
        {
            var merchant = await MerchantOfAsync(db, Email);
            return await db.BankAccounts.Where(b => b.MerchantId == merchant.Id).Select(b => b.PeyBankAccountId).SingleAsync();
        });

    private static string PhoneInputTag(string html)
    {
        var match = PhoneInputRegex().Match(html);
        Assert.True(match.Success, "No se encontró el campo del celular.");
        return match.Value;
    }

    [GeneratedRegex("<input[^>]*name=\"Input.PhoneNumber\"[^>]*>")]
    private static partial Regex PhoneInputRegex();

    private static async Task<string> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }
}
