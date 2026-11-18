using System.Globalization;
using Local133.Application.Payments;
using Local133.Application.Sales;
using Local133.Domain.Pey;
using Local133.Domain.Sales;
using Local133.Domain.Settings;
using Local133.Infrastructure.Jobs;
using Local133.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Local133.IntegrationTests.Sales;

/// <summary>
/// Paso 2.9: conciliación de pagos (job de Hangfire cada minuto, aquí llamado directo). Respaldo del webhook: pregunta
/// a Pey (simulado) por las ventas sin pagar y registra los pagos que falten.
/// </summary>
public class PaymentReconciliationJobTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    // --- QR ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Pago_de_QR_sin_webhook_se_registra_con_su_comision_y_avisa_al_vendedor()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client, "150.00");
        await AgeAsync(sale.Id, minutesAgo: 3);
        var transactionId = NewTransactionId();
        var paidAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        Factory.Gateway.CodiQrTransactions[sale.PeyQrId!] = [FakePaymentGateway.Transaction(transactionId, 150m, External(sale), paidAt: paidAt)];

        Assert.Equal(1, await RunJobAsync());

        Assert.Equal([(sale.PeyQrId!, (string?)null)], Factory.Gateway.CodiQrTransactionLookups.ToArray());
        var payment = Assert.Single(await PaymentsAsync());
        Assert.Equal(transactionId, payment.PeyTransactionId);
        Assert.Equal(sale.Id, payment.SaleId);
        Assert.Equal(150m, payment.Amount);
        Assert.Equal(paidAt.ToUnixTimeSeconds(), payment.PaidAt.ToUnixTimeSeconds());
        Assert.Equal("0192191044", payment.TrackingKey);
        Assert.Equal(SystemSettings.DefaultCommissionRate, payment.CommissionRate);
        Assert.Equal(4.50m, payment.CommissionAmount);
        var reloaded = await LoadSaleAsync(sale.Id);
        Assert.Equal(SaleStatus.Paid, reloaded.Status);
        Assert.NotNull(reloaded.PaymentCheckedAt);
        Assert.Equal((sale.MerchantId, sale.Id), Assert.Single(Factory.Notifier.Notifications));
    }

    [Theory]
    [InlineData(SaleStatus.Expired)]
    [InlineData(SaleStatus.Cancelled)]
    public async Task La_consulta_final_recupera_el_pago_de_una_venta_vencida_o_cancelada(SaleStatus status)
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);
        await AgeAsync(sale.Id, minutesAgo: 21, checkedMinutesAfterCreation: 8, status: status);
        Factory.Gateway.CodiQrTransactions[sale.PeyQrId!] = [FakePaymentGateway.Transaction(NewTransactionId(), sale.Amount, External(sale))];

        Assert.Equal(1, await RunJobAsync());

        Assert.Equal(SaleStatus.Paid, (await LoadSaleAsync(sale.Id)).Status);
        Assert.Single(await PaymentsAsync());
    }

    [Fact]
    public async Task Sin_pagos_sella_la_consulta_y_no_vuelve_a_preguntar_hasta_la_consulta_final()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);
        await AgeAsync(sale.Id, minutesAgo: 3);

        Assert.Equal(1, await RunJobAsync());
        Assert.NotNull((await LoadSaleAsync(sale.Id)).PaymentCheckedAt);
        Assert.Equal(0, await RunJobAsync());
        Assert.Single(Factory.Gateway.CodiQrTransactionLookups);

        // Ya vencida: toca la consulta final (la última fue mientras estaba vigente) y después ya no.
        await AgeAsync(sale.Id, minutesAgo: 21, checkedMinutesAfterCreation: 8);
        Assert.Equal(1, await RunJobAsync());
        Assert.Equal(0, await RunJobAsync());

        Assert.Equal(2, Factory.Gateway.CodiQrTransactionLookups.Count);
        Assert.Empty(await PaymentsAsync());
        Assert.NotEqual(SaleStatus.Paid, (await LoadSaleAsync(sale.Id)).Status);
    }

    [Fact]
    public async Task Sigue_las_paginas_de_pagos_del_QR()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client, "150.00");
        await AgeAsync(sale.Id, minutesAgo: 3);
        Factory.Gateway.CodiQrPageSize = 1;
        Factory.Gateway.CodiQrTransactions[sale.PeyQrId!] =
        [
            FakePaymentGateway.Transaction(NewTransactionId(), 150m, External(sale)),
            FakePaymentGateway.Transaction(NewTransactionId(), 150m, External(sale)),
        ];

        Assert.Equal(1, await RunJobAsync());

        Assert.Equal([(sale.PeyQrId!, (string?)null), (sale.PeyQrId!, "1")], Factory.Gateway.CodiQrTransactionLookups.ToArray());
        Assert.Equal(2, (await PaymentsAsync()).Count);
    }

    [Fact]
    public async Task Un_pago_pendiente_en_Pey_no_se_registra()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);
        await AgeAsync(sale.Id, minutesAgo: 3);
        Factory.Gateway.CodiQrTransactions[sale.PeyQrId!] =
            [FakePaymentGateway.Transaction(NewTransactionId(), sale.Amount, External(sale), PeyTransactionStatus.Pending)];

        Assert.Equal(1, await RunJobAsync());

        Assert.Empty(await PaymentsAsync());
        Assert.Equal(SaleStatus.Pending, (await LoadSaleAsync(sale.Id)).Status);
    }

    // --- Cobro por celular ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Cobro_por_celular_pagado_se_registra_consultando_su_transaccion()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeByPhoneAsync(client);
        await AgeAsync(sale.Id, minutesAgo: 3);
        Factory.Gateway.Transactions[sale.PeyTransactionId!] = FakePaymentGateway.Transaction(
            sale.PeyTransactionId!, sale.Amount, External(sale), payableType: PeyPayableType.ApiCodiPn);

        Assert.Equal(1, await RunJobAsync());

        Assert.Equal([sale.PeyTransactionId!], Factory.Gateway.TransactionLookups.ToArray());
        Assert.Empty(Factory.Gateway.CodiQrTransactionLookups);
        var payment = Assert.Single(await PaymentsAsync());
        Assert.Equal(sale.PeyTransactionId, payment.PeyTransactionId);
        Assert.Equal(SaleStatus.Paid, (await LoadSaleAsync(sale.Id)).Status);
    }

    [Fact]
    public async Task Cobro_por_celular_sin_pagar_solo_sella_la_consulta()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeByPhoneAsync(client);
        await AgeAsync(sale.Id, minutesAgo: 3);
        Factory.Gateway.Transactions[sale.PeyTransactionId!] = FakePaymentGateway.Transaction(
            sale.PeyTransactionId!, sale.Amount, External(sale), PeyTransactionStatus.Pending, payableType: PeyPayableType.ApiCodiPn);

        Assert.Equal(1, await RunJobAsync());

        Assert.Empty(await PaymentsAsync());
        var reloaded = await LoadSaleAsync(sale.Id);
        Assert.Equal(SaleStatus.Pending, reloaded.Status);
        Assert.NotNull(reloaded.PaymentCheckedAt);
    }

    [Fact]
    public async Task Transaccion_que_Pey_no_encuentra_sella_la_consulta_sin_registrar_nada()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeByPhoneAsync(client);
        await AgeAsync(sale.Id, minutesAgo: 3);

        Assert.Equal(1, await RunJobAsync());

        Assert.Single(Factory.Gateway.TransactionLookups);
        Assert.Empty(await PaymentsAsync());
        Assert.NotNull((await LoadSaleAsync(sale.Id)).PaymentCheckedAt);
    }

    [Fact]
    public async Task Cobro_por_celular_sin_transaccion_confirmada_no_se_consulta()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeByPhoneAsync(client);
        await AgeAsync(sale.Id, minutesAgo: 21, status: SaleStatus.Cancelled);
        await UpdateSaleAsync(sale.Id, s => s.PeyTransactionId = null);

        Assert.Equal(0, await RunJobAsync());

        Assert.Empty(Factory.Gateway.TransactionLookups);
    }

    // --- Idempotencia con el webhook --------------------------------------------------------------------------------

    [Fact]
    public async Task Si_despues_llega_el_webhook_del_mismo_pago_no_se_duplica()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);
        await AgeAsync(sale.Id, minutesAgo: 3);
        var transactionId = NewTransactionId();
        Factory.Gateway.CodiQrTransactions[sale.PeyQrId!] = [FakePaymentGateway.Transaction(transactionId, sale.Amount, External(sale))];
        await RunJobAsync();

        var response = await PostWebhookAsync(QrPaymentBodyFor(sale, transactionId));

        Assert.Equal("Processed", await WebhookStatusAsync(response));
        Assert.Single(await PaymentsAsync());
        Assert.Single(Factory.Notifier.Notifications);
    }

    [Fact]
    public async Task Venta_pagada_por_webhook_ya_no_se_consulta()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);
        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(QrPaymentBodyFor(sale))));
        await AgeAsync(sale.Id, minutesAgo: 3, status: SaleStatus.Paid);

        Assert.Equal(0, await RunJobAsync());

        Assert.Empty(Factory.Gateway.CodiQrTransactionLookups);
    }

    // --- Fallas, aislamiento y ventas que no se consultan -----------------------------------------------------------

    [Fact]
    public async Task Si_Pey_falla_con_una_venta_no_la_sella_y_sigue_con_las_demas()
    {
        var client = await ActiveMerchantAsync(Email);
        var failing = await ChargeAsync(client);
        var paid = await ChargeAsync(client);
        await AgeAsync(failing.Id, minutesAgo: 4);
        await AgeAsync(paid.Id, minutesAgo: 3);
        Factory.Gateway.TransactionLookupExceptions[failing.PeyQrId!] = new PaymentGatewayUnavailableException("timeout");
        Factory.Gateway.CodiQrTransactions[paid.PeyQrId!] = [FakePaymentGateway.Transaction(NewTransactionId(), paid.Amount, External(paid))];

        Assert.Equal(1, await RunJobAsync());

        var failingReloaded = await LoadSaleAsync(failing.Id);
        Assert.Equal(SaleStatus.Pending, failingReloaded.Status);
        Assert.Null(failingReloaded.PaymentCheckedAt);
        Assert.Equal(SaleStatus.Paid, (await LoadSaleAsync(paid.Id)).Status);

        // La siguiente corrida vuelve a intentar la que falló.
        Factory.Gateway.TransactionLookupExceptions.Clear();
        Assert.Equal(1, await RunJobAsync());
        Assert.NotNull((await LoadSaleAsync(failing.Id)).PaymentCheckedAt);
    }

    [Fact]
    public async Task Pago_con_metadata_de_otro_vendedor_no_se_registra()
    {
        var client = await ActiveMerchantAsync(Email);
        await ActiveMerchantAsync(OtherEmail);
        var otherMerchantId = await MerchantIdAsync(OtherEmail);
        var sale = await ChargeAsync(client);
        await AgeAsync(sale.Id, minutesAgo: 3);
        var external = External(sale);
        external[SaleMetadata.MerchantIdKey] = otherMerchantId.ToString(CultureInfo.InvariantCulture);
        Factory.Gateway.CodiQrTransactions[sale.PeyQrId!] = [FakePaymentGateway.Transaction(NewTransactionId(), sale.Amount, external)];

        Assert.Equal(1, await RunJobAsync());

        Assert.Empty(await PaymentsAsync());
        Assert.Equal(SaleStatus.Pending, (await LoadSaleAsync(sale.Id)).Status);
        Assert.Empty(Factory.Notifier.Notifications);
    }

    [Fact]
    public async Task No_consulta_ventas_recien_creadas_ni_sin_vencimiento_ni_vencidas_hace_mas_de_un_dia()
    {
        var client = await ActiveMerchantAsync(Email);
        await ChargeAsync(client); // recién creada
        var noExpiration = await ChargeAsync(client);
        await AgeAsync(noExpiration.Id, minutesAgo: 3);
        await UpdateSaleAsync(noExpiration.Id, s => s.ExpiresAt = null);
        var old = await ChargeAsync(client);
        await AgeAsync(old.Id, minutesAgo: 60 * 25, status: SaleStatus.Expired);

        Assert.Equal(0, await RunJobAsync());

        Assert.Empty(Factory.Gateway.CodiQrTransactionLookups);
    }

    // --- Ayudas -----------------------------------------------------------------------------------------------------

    private async Task<int> RunJobAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentReconciliationJob>().RunAsync(CancellationToken.None);
    }

    /// <summary>
    /// Mueve la venta al pasado: creada hace <paramref name="minutesAgo"/> minutos con QR de 10 minutos y, si se indica,
    /// consultada <paramref name="checkedMinutesAfterCreation"/> minutos después de crearse.
    /// </summary>
    private Task AgeAsync(int saleId, double minutesAgo, double? checkedMinutesAfterCreation = null, SaleStatus? status = null) =>
        UpdateSaleAsync(saleId, s =>
        {
            var created = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo);
            s.CreatedAt = created;
            s.ExpiresAt = created.AddMinutes(10);
            s.PaymentCheckedAt = checkedMinutesAfterCreation is { } minutes ? created.AddMinutes(minutes) : null;
            if (status is { } newStatus)
            {
                s.Status = newStatus;
            }
        });

    private static Dictionary<string, string?> External(Sale sale) =>
        SaleMetadataFor(sale).ToDictionary(p => p.Key, p => (string?)p.Value);

    private Task<List<Domain.Payments.Payment>> PaymentsAsync() =>
        WithDbAsync(db => db.Payments.AsNoTracking().OrderBy(p => p.Id).ToListAsync());
}
