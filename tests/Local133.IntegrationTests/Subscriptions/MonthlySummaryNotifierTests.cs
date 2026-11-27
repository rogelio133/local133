using System.Net;
using Local133.Domain.Merchants;
using Local133.Domain.Notifications;
using Local133.Domain.Sales;
using Local133.Infrastructure.Subscriptions;
using Local133.IntegrationTests.Infrastructure;
using Local133.IntegrationTests.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Local133.IntegrationTests.Subscriptions;

/// <summary>
/// 5.10: contenido del resumen mensual que el job pide el día 1. Usa la clase concreta: la fábrica cambia
/// <c>IMonthlySummaryNotifier</c> por un registrador para las pruebas del job.
/// </summary>
public class MonthlySummaryNotifierTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    private static readonly DateOnly October = new(2026, 10, 1);

    // Hora de México (UTC−6): el mes de octubre va del 1 a las 00:00 (06:00 UTC) al 31 a las 23:59 (1 de nov, 05:59 UTC).
    private static readonly DateTimeOffset FirstMinuteOfOctober = new(2026, 10, 1, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LastMinuteOfOctober = new(2026, 11, 1, 5, 59, 0, TimeSpan.Zero);

    [Fact]
    public async Task Suma_los_cobros_del_mes_en_hora_de_Mexico_y_avisa_con_correo()
    {
        await ActiveMerchantAsync(Email);
        await SeedTrialAsync(Email);
        await PaidSaleAsync(Email, 10_000m, FirstMinuteOfOctober);
        var twoPayments = await PaidSaleAsync(Email, 8_000m, LastMinuteOfOctober);
        await AddPaymentAsync(twoPayments, 400m, LastMinuteOfOctober); // dos pagos, una sola venta
        await PaidSaleAsync(Email, 999m, FirstMinuteOfOctober.AddMinutes(-1)); // 30 de septiembre
        await PaidSaleAsync(Email, 999m, LastMinuteOfOctober.AddMinutes(1)); // 1 de noviembre

        await SendAsync(October);

        var notification = Assert.Single(await NotificationsAsync());
        Assert.Equal(NotificationType.MonthlySummary, notification.Type);
        Assert.Equal("summary:2026-10", notification.DedupKey);
        Assert.Equal("Tu resumen de octubre", notification.Title);
        // 18,400 × 3.5 % × 1.16 = 747.04 ⇒ $747 (el ejemplo del diseño).
        Assert.Equal(
            "En octubre cobraste $18,400 en 2 ventas. Con una terminal habrías pagado unos $747 de comisión " +
            "(estimación con 3.5 % + IVA). Tu plan Pro cuesta $199 al mes.",
            notification.Body);
        Assert.Equal("/app/ventas", notification.Url);
        Assert.NotNull(notification.EmailSentAt);

        var email = Factory.Emails.Last(Email);
        Assert.Equal("Tu resumen de octubre · Local133", email.Subject);
        Assert.Equal("http://localhost:5231/app/ventas", email.Link);
        var html = WebUtility.HtmlDecode(email.Html);
        Assert.Contains("¡Hola, María!", html);
        Assert.Contains("En octubre cobraste $18,400 en 2 ventas.", html);
        Assert.Contains("estimación con 3.5 % + IVA", html);
    }

    [Fact]
    public async Task Al_vencido_le_dice_cuanto_pagaria_con_Basico_y_lo_lleva_al_checkout()
    {
        await ActiveMerchantAsync(Email);
        await SeedExpiredPlanAsync(Email);
        await PaidSaleAsync(Email, 150.50m, FirstMinuteOfOctober.AddDays(3));

        await SendAsync(October);

        var notification = Assert.Single(await NotificationsAsync());
        Assert.StartsWith("En octubre cobraste $150.50 en 1 venta. Con una terminal habrías pagado unos $6 de comisión", notification.Body);
        Assert.Contains("Tu plan venció: con Básico pagarías $99 al mes y vuelves a cobrar", notification.Body);
        Assert.Equal("/app/plan/checkout#basico", notification.Url);
        var email = Factory.Emails.Last(Email);
        Assert.Equal("http://localhost:5231/app/plan/checkout#basico", email.Link);
        Assert.Contains("Elegir mi plan", email.Html);
    }

    [Fact]
    public async Task Con_Basico_compara_con_el_precio_de_Basico()
    {
        await ActiveMerchantAsync(Email);
        await SeedBasicPlanAsync(Email);
        await PaidSaleAsync(Email, 1_000m, FirstMinuteOfOctober.AddDays(3));

        await SendAsync(October);

        var notification = Assert.Single(await NotificationsAsync());
        Assert.EndsWith("Tu plan Básico cuesta $99 al mes.", notification.Body);
        Assert.Equal("/app/ventas", notification.Url);
    }

    [Fact]
    public async Task Solo_a_vendedores_con_cobros_que_no_esten_dados_de_baja()
    {
        await ActiveMerchantAsync(Email);
        await ActiveMerchantAsync(OtherEmail);
        await ActiveMerchantAsync("tercero@prueba.mx");
        await PaidSaleAsync(Email, 100m, FirstMinuteOfOctober.AddDays(1));
        await PaidSaleAsync(OtherEmail, 100m, FirstMinuteOfOctober.AddDays(1));
        await UpdateMerchantAsync(OtherEmail, m => m.Status = MerchantStatus.Disabled);
        // Sin cobros en octubre: una venta sin pagar.
        await InsertSaleAsync("tercero@prueba.mx", 100m, FirstMinuteOfOctober.AddDays(1));

        await SendAsync(October);

        var notification = Assert.Single(await NotificationsAsync());
        Assert.Equal(await MerchantIdAsync(Email), notification.MerchantId);
        Assert.DoesNotContain(Factory.Emails.Sent, e => e.To == OtherEmail);
    }

    [Fact]
    public async Task Repetir_el_mes_no_duplica_el_aviso_ni_el_correo()
    {
        await ActiveMerchantAsync(Email);
        await PaidSaleAsync(Email, 100m, FirstMinuteOfOctober.AddDays(1));

        await SendAsync(October);
        await SendAsync(October);

        Assert.Single(await NotificationsAsync());
        Assert.Single(Factory.Emails.Sent, e => e.Subject.StartsWith("Tu resumen", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Si_falla_el_correo_queda_el_aviso_y_sigue_con_los_demas()
    {
        await ActiveMerchantAsync(Email);
        await ActiveMerchantAsync(OtherEmail);
        await PaidSaleAsync(Email, 100m, FirstMinuteOfOctober.AddDays(1));
        await PaidSaleAsync(OtherEmail, 100m, FirstMinuteOfOctober.AddDays(1));
        Factory.Emails.FailWith = new InvalidOperationException("Resend caído");

        await SendAsync(October);

        var notifications = await NotificationsAsync();
        Assert.Equal(2, notifications.Count);
        Assert.All(notifications, n => Assert.Null(n.EmailSentAt));
    }

    private async Task<Sale> PaidSaleAsync(string email, decimal amount, DateTimeOffset paidAt)
    {
        var sale = await InsertSaleAsync(email, amount, paidAt.AddMinutes(-1));
        await AddPaymentAsync(sale, amount, paidAt);
        return sale;
    }

    private async Task SendAsync(DateOnly month)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MonthlySummaryNotifier>().SendAsync(month);
    }

    private Task<List<Notification>> NotificationsAsync() =>
        WithDbAsync(db => db.Notifications.IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.Type == NotificationType.MonthlySummary)
            .OrderBy(n => n.Id).ToListAsync());
}
