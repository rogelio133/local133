using System.Net;
using Local133.Application.Auth;
using Local133.Domain.Subscriptions;
using Local133.Domain.Webhooks;
using Local133.IntegrationTests.Infrastructure;
using Local133.IntegrationTests.Subscriptions;

namespace Local133.IntegrationTests.Admin;

/// <summary>Ayudas de las pruebas del admin de la 6.4 (tablero, transacciones y webhooks).</summary>
public abstract class AdminTestBase(Local133WebFactory factory) : SubscriptionPaymentTestBase(factory)
{
    protected const string AdminEmail = "admin@prueba.mx";

    protected async Task<HttpClient> AdminAsync()
    {
        await CreateConfirmedUserAsync(AdminEmail, Roles.Admin);
        var client = CreateClient();
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(client, AdminEmail)).StatusCode);
        return client;
    }

    /// <summary>Cargo de la suscripción insertado directo; con <paramref name="paidAt"/> queda pagado (a mano si se indica).</summary>
    protected Task<SubscriptionCharge> InsertChargeAsync(
        string email, decimal amount, DateTimeOffset createdAt, DateTimeOffset? paidAt = null, bool manual = false,
        SubscriptionChargeStatus status = SubscriptionChargeStatus.Pending) => WithDbAsync(async db =>
    {
        var merchant = await MerchantOfAsync(db, email);
        var charge = SubscriptionCharge.Create(
            merchant.Id, new SubscriptionQuote(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, 1, amount, 0m, null, amount), false,
            SubscriptionChargeOrigin.Merchant, createdAt);
        charge.AttachLink($"plink_{Guid.NewGuid():N}"[..20], "https://pey.mx/link");
        charge.Status = status;
        if (paidAt is { } paid)
        {
            charge.MarkPaid(paid, manual ? paid : null);
        }

        db.SubscriptionCharges.Add(charge);
        await db.SaveChangesAsync();
        return charge;
    });

    /// <summary>Pago de Pey de un cargo (fila de <c>SubscriptionPayments</c>).</summary>
    protected Task<SubscriptionPayment> InsertSubscriptionPaymentAsync(
        SubscriptionCharge charge, string method, decimal amount, DateTimeOffset paidAt, decimal? fee = null) => WithDbAsync(async db =>
    {
        var payment = SubscriptionPayment.Record(charge, NewTransactionId(), method, amount + (fee ?? 0m), amount, fee, null, paidAt, paidAt);
        db.SubscriptionPayments.Add(payment);
        await db.SaveChangesAsync();
        return payment;
    });

    /// <summary>Evento de la bitácora insertado directo (para estatus y fechas a la medida).</summary>
    protected Task<WebhookEvent> InsertWebhookEventAsync(
        string type, string payload, DateTimeOffset receivedAt, WebhookEventStatus status, string? error = null) => WithDbAsync(async db =>
    {
        var webhook = WebhookEvent.Receive(NewWebhookId(), type, payload, receivedAt, receivedAt);
        webhook.StartAttempt();
        switch (status)
        {
            case WebhookEventStatus.Processed:
                webhook.MarkProcessed(receivedAt);
                break;
            case WebhookEventStatus.Ignored:
                webhook.MarkIgnored(error ?? "Sin acción", receivedAt);
                break;
            case WebhookEventStatus.Failed:
                webhook.MarkFailed(error ?? "Venta no encontrada", receivedAt);
                break;
        }

        db.WebhookEvents.Add(webhook);
        await db.SaveChangesAsync();
        return webhook;
    });
}
