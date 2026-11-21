using System.Globalization;
using System.Net;
using System.Text.Json;
using Local133.Application.Subscriptions;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using Local133.IntegrationTests.Infrastructure;
using Local133.IntegrationTests.Sales;
using Microsoft.EntityFrameworkCore;

namespace Local133.IntegrationTests.Subscriptions;

/// <summary>Ayudas del pago de la suscripción: «Mi plan», cargos, links de Pey simulados y webhooks de links firmados.</summary>
public abstract class SubscriptionPaymentTestBase(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    protected const string PlanUrl = "/app/plan";

    /// <summary>Vendedor activo en su prueba de Pro (empezó hace 7 días), con sesión.</summary>
    protected async Task<HttpClient> TrialMerchantAsync(string email)
    {
        var client = await ActiveMerchantAsync(email);
        await SeedTrialAsync(email);
        return client;
    }

    /// <summary>POST de «Pagar» con el paquete (token de «Mi plan»).</summary>
    protected static Task<HttpResponseMessage> PayAsync(HttpClient client, SubscriptionPackage package) =>
        PostFormAsync(client, PlanUrl, new Dictionary<string, string> { ["package"] = package.ToString() }, postUrl: "/app/plan/pagar");

    /// <summary>Paga (Pey simulado) y devuelve el cargo creado o reutilizado.</summary>
    protected async Task<SubscriptionCharge> CreateChargeAsync(HttpClient client, SubscriptionPackage package = SubscriptionPackage.OneMonth)
    {
        var response = await PayAsync(client, package);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var url = response.Headers.Location!.AbsoluteUri;
        return await WithDbAsync(db => db.SubscriptionCharges.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(c => c.Url == url));
    }

    protected Task<SubscriptionCharge> LoadChargeAsync(int id) => WithDbAsync(db =>
        db.SubscriptionCharges.IgnoreQueryFilters().AsNoTracking().Include(c => c.Payments).SingleAsync(c => c.Id == id));

    protected Task<List<SubscriptionCharge>> ChargesAsync() => WithDbAsync(db =>
        db.SubscriptionCharges.IgnoreQueryFilters().AsNoTracking().OrderBy(c => c.Id).ToListAsync());

    protected Task<Subscription> LoadSubscriptionAsync(string email) => WithDbAsync(async db =>
    {
        var merchant = await MerchantOfAsync(db, email);
        return await db.Subscriptions.IgnoreQueryFilters().AsNoTracking().Include(s => s.Periods)
            .SingleAsync(s => s.MerchantId == merchant.Id);
    });

    protected Task UpdateChargeAsync(int id, Action<SubscriptionCharge> change) => WithDbAsync(async db =>
    {
        change(await db.SubscriptionCharges.IgnoreQueryFilters().SingleAsync(c => c.Id == id));
        await db.SaveChangesAsync();
    });

    /// <summary>
    /// Cambia <c>SystemSettings</c> durante <paramref name="action"/> y lo restablece al final (Respawn no limpia esa
    /// tabla: un cambio sin restablecer contamina a las demás pruebas).
    /// </summary>
    protected async Task WithSettingsAsync(Action<SystemSettings> change, Func<Task> action)
    {
        var original = await WithDbAsync(db => db.SystemSettings.AsNoTracking().SingleAsync(s => s.Id == SystemSettings.SingletonId));
        await WithDbAsync(async db =>
        {
            change(await db.SystemSettings.SingleAsync(s => s.Id == SystemSettings.SingletonId));
            await db.SaveChangesAsync();
        });
        try
        {
            await action();
        }
        finally
        {
            await WithDbAsync(async db =>
            {
                var settings = await db.SystemSettings.SingleAsync(s => s.Id == SystemSettings.SingletonId);
                db.Entry(settings).CurrentValues.SetValues(original);
                await db.SaveChangesAsync();
            });
        }
    }

    /// <summary>Webhook <c>payment_link.status_updated</c> del cargo, con la metadata que le pusimos al crear el link.</summary>
    protected static string LinkWebhookBody(
        SubscriptionCharge charge, string status, params (string TransactionId, decimal Base)[] transactions) =>
        LinkWebhookBody(charge.PeyPaymentLinkId!, status, charge.ClientReferenceId, MetadataOf(charge), transactions);

    protected static string LinkWebhookBody(
        string paymentLinkId, string status, string? clientReferenceId, IReadOnlyDictionary<string, string>? metadata,
        params (string TransactionId, decimal Base)[] transactions) =>
        JsonSerializer.Serialize(new
        {
            type = "payment_link.status_updated",
            apiKeyId = "01HXYZKEY",
            paymentLinkId,
            status,
            clientReferenceId,
            transactions = transactions.Select(t => new
            {
                transactionId = t.TransactionId,
                paymentMethod = "CODI",
                datePaid = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                amount = t.Base,
                amountBreakdown = new { @base = t.Base, tip = 0m, fee = 0m },
                applicationFee = (decimal?)null,
                metadata = new { trackingKey = "RASTREO1", externalMetadata = metadata },
            }),
        });

    protected static Dictionary<string, string> MetadataOf(SubscriptionCharge charge) => new()
    {
        [SubscriptionChargeMetadata.KindKey] = SubscriptionChargeMetadata.SubscriptionKind,
        [SubscriptionChargeMetadata.MerchantIdKey] = charge.MerchantId.ToString(CultureInfo.InvariantCulture),
        [SubscriptionChargeMetadata.SubscriptionChargeIdKey] = charge.Id.ToString(CultureInfo.InvariantCulture),
    };
}
