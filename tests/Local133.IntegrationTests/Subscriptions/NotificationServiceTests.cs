using Local133.Application.Notifications;
using Local133.Application.Tenancy;
using Local133.Domain.Notifications;
using Local133.Infrastructure.Persistence;
using Local133.IntegrationTests.Infrastructure;
using Local133.IntegrationTests.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Local133.IntegrationTests.Subscriptions;

/// <summary>Base de los avisos (5.4 adelantada en la 5.7): idempotencia por <c>DedupKey</c>, correo y aislamiento.</summary>
public class NotificationServiceTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    private const string Subject = "Aviso de prueba · Local133";

    [Fact]
    public async Task La_misma_llave_no_repite_el_aviso_ni_el_correo()
    {
        await CreateMerchantAsync(Email);
        var merchantId = await MerchantIdAsync(Email);

        var first = await NotifyAsync(Request(merchantId, "prueba:1"));
        var second = await NotifyAsync(Request(merchantId, "prueba:1"));

        var created = Assert.IsType<NotificationResult.Created>(first);
        Assert.True(created.EmailSent);
        Assert.IsType<NotificationResult.AlreadyExists>(second);
        var notification = Assert.Single(await NotificationsAsync());
        Assert.Equal(NotificationType.SubscriptionUpcoming, notification.Type);
        Assert.Equal("/app/plan", notification.Url);
        Assert.NotNull(notification.EmailSentAt);
        var email = Assert.Single(Factory.Emails.Sent, e => e.Subject == Subject);
        Assert.Equal(Email, email.To);
        Assert.Equal("http://localhost:5231/app/plan", email.Link);
    }

    [Fact]
    public async Task La_misma_llave_en_otro_vendedor_es_otro_aviso()
    {
        await CreateMerchantAsync(Email);
        await CreateMerchantAsync(OtherEmail);

        await NotifyAsync(Request(await MerchantIdAsync(Email), "prueba:1"));
        await NotifyAsync(Request(await MerchantIdAsync(OtherEmail), "prueba:1"));

        Assert.Equal(2, (await NotificationsAsync()).Count);
        Assert.Equal(2, Factory.Emails.Sent.Count(e => e.Subject == Subject));
    }

    [Fact]
    public async Task Si_falla_el_correo_el_aviso_queda_y_no_lanza()
    {
        await CreateMerchantAsync(Email);
        Factory.Emails.FailWith = new InvalidOperationException("Resend caído");

        var result = await NotifyAsync(Request(await MerchantIdAsync(Email), "prueba:1"));

        Assert.False(Assert.IsType<NotificationResult.Created>(result).EmailSent);
        Assert.Null(Assert.Single(await NotificationsAsync()).EmailSentAt);
    }

    [Fact]
    public async Task Sin_correo_solo_queda_el_aviso()
    {
        await CreateMerchantAsync(Email);

        var result = await NotifyAsync(Request(await MerchantIdAsync(Email), "prueba:1") with { Email = null });

        Assert.False(Assert.IsType<NotificationResult.Created>(result).EmailSent);
        Assert.DoesNotContain(Factory.Emails.Sent, e => e.Subject == Subject);
    }

    [Fact]
    public async Task Un_vendedor_solo_ve_sus_avisos()
    {
        await CreateMerchantAsync(Email);
        await CreateMerchantAsync(OtherEmail);
        var uno = await MerchantIdAsync(Email);
        var dos = await MerchantIdAsync(OtherEmail);
        await NotifyAsync(Request(uno, "prueba:1"));
        await NotifyAsync(Request(dos, "prueba:2"));

        using var scope = Factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();
        await using var asUno = new AppDbContext(options, new FixedTenant(uno));

        Assert.Equal(uno, Assert.Single(await asUno.Notifications.ToListAsync()).MerchantId);
    }

    private static NotificationRequest Request(int merchantId, string key) => new(
        merchantId, NotificationType.SubscriptionUpcoming, "Tu plan Pro vence en 3 días", "Vence el 20 de diciembre.",
        "/app/plan", key,
        new NotificationEmail(Subject, "Vence pronto", "Tu plan vence", ["¡Hola!"], "Ver mi plan", "/app/plan", null));

    private async Task<NotificationResult> NotifyAsync(NotificationRequest request)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<INotificationService>().NotifyAsync(request);
    }

    private Task<List<Notification>> NotificationsAsync() =>
        WithDbAsync(db => db.Notifications.IgnoreQueryFilters().AsNoTracking().OrderBy(n => n.Id).ToListAsync());

    private sealed class FixedTenant(int merchantId) : ITenantContext
    {
        public bool IsMerchantScoped => true;

        public int? MerchantId => merchantId;
    }
}
