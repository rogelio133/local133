using Local133.Application.Payments;
using Local133.Domain.Pey;
using Local133.Domain.Subscriptions;
using Local133.Infrastructure.Jobs;
using Local133.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Local133.IntegrationTests.Subscriptions;

/// <summary>5.6: job <c>conciliar-links</c>, respaldo del webhook de links con <c>GET /payment-link/{id}</c>.</summary>
public class PaymentLinkReconciliationJobTests(Local133WebFactory factory) : SubscriptionPaymentTestBase(factory)
{
    [Fact]
    public async Task Abona_un_link_pagado_cuyo_webhook_se_perdio()
    {
        var client = await TrialMerchantAsync(Email);
        var charge = await CreateChargeAsync(client);
        await AgeAsync(charge.Id, minutes: 15);
        Factory.Gateway.PaymentLinks[charge.PeyPaymentLinkId!] = FakePaymentGateway.PaymentLink(
            charge.PeyPaymentLinkId!, charge.Amount, PeyPaymentLinkStatus.Paid, charge.ClientReferenceId, null,
            (NewTransactionId(), charge.Amount));

        var checkedCount = await RunJobAsync();

        Assert.Equal(1, checkedCount);
        var paid = await LoadChargeAsync(charge.Id);
        Assert.Equal(SubscriptionChargeStatus.Paid, paid.Status);
        Assert.NotNull(paid.PaymentCheckedAt);
        Assert.Single((await LoadSubscriptionAsync(Email)).Periods, p => p.SubscriptionChargeId == charge.Id);
    }

    [Fact]
    public async Task No_consulta_un_cargo_recien_creado_ni_uno_pagado()
    {
        var client = await TrialMerchantAsync(Email);
        var fresh = await CreateChargeAsync(client);
        var paid = await CreateChargeAsync(client, SubscriptionPackage.Annual);
        await AgeAsync(paid.Id, minutes: 30);
        await PostWebhookAsync(LinkWebhookBody(paid, "paid", (NewTransactionId(), paid.Amount)));

        var checkedCount = await RunJobAsync();

        Assert.Equal(0, checkedCount);
        Assert.Empty(Factory.Gateway.PaymentLinkLookups);
        Assert.Equal(SubscriptionChargeStatus.Pending, (await LoadChargeAsync(fresh.Id)).Status);
    }

    [Fact]
    public async Task Un_link_que_Pey_no_conoce_se_sella_y_una_falla_no()
    {
        var client = await TrialMerchantAsync(Email);
        var unknown = await CreateChargeAsync(client);
        var failing = await CreateChargeAsync(client, SubscriptionPackage.Annual);
        await AgeAsync(unknown.Id, minutes: 15);
        await AgeAsync(failing.Id, minutes: 15);
        Factory.Gateway.PaymentLinkLookupExceptions[failing.PeyPaymentLinkId!] = new PaymentGatewayUnavailableException("timeout");

        var checkedCount = await RunJobAsync();

        Assert.Equal(1, checkedCount);
        Assert.NotNull((await LoadChargeAsync(unknown.Id)).PaymentCheckedAt);
        Assert.Null((await LoadChargeAsync(failing.Id)).PaymentCheckedAt);
        Assert.Equal(2, Factory.Gateway.PaymentLinkLookups.Count);
    }

    [Fact]
    public async Task Respeta_el_calendario_despues_de_consultar()
    {
        var client = await TrialMerchantAsync(Email);
        var charge = await CreateChargeAsync(client);
        await AgeAsync(charge.Id, minutes: 15);
        Factory.Gateway.PaymentLinks[charge.PeyPaymentLinkId!] = FakePaymentGateway.PaymentLink(
            charge.PeyPaymentLinkId!, charge.Amount, PeyPaymentLinkStatus.Pending);

        var first = await RunJobAsync();
        var second = await RunJobAsync();

        Assert.Equal(1, first);
        Assert.Equal(0, second); // la siguiente consulta toca en 10 min
        Assert.Single(Factory.Gateway.PaymentLinkLookups);
    }

    private Task AgeAsync(int chargeId, double minutes) =>
        UpdateChargeAsync(chargeId, c => c.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-minutes));

    private async Task<int> RunJobAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentLinkReconciliationJob>().RunAsync(CancellationToken.None);
    }
}
