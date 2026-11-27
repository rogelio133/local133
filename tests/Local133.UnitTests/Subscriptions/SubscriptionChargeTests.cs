using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
using static Local133.UnitTests.Subscriptions.PlanStatusTests;

namespace Local133.UnitTests.Subscriptions;

/// <summary>5.6: cargo de la suscripción (un link por cargo), cotización por vendedor y calendario de conciliación.</summary>
public class SubscriptionChargeTests
{
    private const int GraceDays = 5;
    private static readonly SystemSettings Settings = new();

    // --- Cargo -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void Nace_pendiente_con_el_precio_cotizado()
    {
        var quote = SubscriptionPricing.Quote(SubscriptionPlan.Pro, SubscriptionPackage.ThreeMonths, Settings);

        var charge = SubscriptionCharge.Create(7, quote, isFounderPrice: false, SubscriptionChargeOrigin.Merchant, Mx(2026, 11, 20, 10));

        Assert.Equal(SubscriptionChargeStatus.Pending, charge.Status);
        Assert.Equal(3, charge.Months);
        Assert.Equal(597m, charge.ListPrice);
        Assert.Equal(597m, charge.Amount);
        Assert.Null(charge.PeyPaymentLinkId);
    }

    [Theory]
    [InlineData("sub-42", true, 42)]
    [InlineData(" sub-7 ", true, 7)]
    [InlineData("sub-0", false, 0)]
    [InlineData("sub--3", false, 0)]
    [InlineData("sub-", false, 0)]
    [InlineData("order-42", false, 0)]
    [InlineData(null, false, 0)]
    public void Lee_la_referencia_del_link(string? reference, bool ok, int expected)
    {
        Assert.Equal(ok, SubscriptionCharge.TryParseClientReference(reference, out var id));
        Assert.Equal(expected, id);
    }

    [Fact]
    public void La_referencia_es_sub_y_el_id()
    {
        Assert.Equal("sub-42", SubscriptionCharge.ClientReferenceFor(42));
    }

    [Fact]
    public void Solo_falla_un_cargo_pendiente_sin_link()
    {
        var charge = NewCharge();
        charge.AttachLink("01PL", "https://pay.pey.mx/01PL");

        Assert.Throws<InvalidOperationException>(() => charge.MarkFailed("x"));

        var other = NewCharge();
        other.MarkFailed(new string('x', 600));
        Assert.Equal(SubscriptionChargeStatus.Failed, other.Status);
        Assert.Equal(SubscriptionCharge.FailureReasonMaxLength, other.FailureReason!.Length);
    }

    [Fact]
    public void El_pago_manda_tambien_desde_fallido_o_incompleto()
    {
        var failed = NewCharge();
        failed.MarkFailed("Pey no respondió.");
        var partial = NewCharge();
        partial.MarkPartial();
        var paidAt = Mx(2026, 11, 20, 12);

        failed.MarkPaid(paidAt);
        partial.MarkPaid(paidAt);

        Assert.Equal(SubscriptionChargeStatus.Paid, failed.Status);
        Assert.Null(failed.FailureReason);
        Assert.Equal(paidAt, partial.PaidAt);
        Assert.Throws<InvalidOperationException>(() => partial.MarkPaid(paidAt));
    }

    [Fact]
    public void Procesando_e_incompleto_no_retroceden_un_pagado()
    {
        var charge = NewCharge();
        charge.MarkPaid(Mx(2026, 11, 20, 12));

        Assert.False(charge.MarkProcessing());
        Assert.False(charge.MarkPartial());
        Assert.Equal(SubscriptionChargeStatus.Paid, charge.Status);
    }

    [Fact]
    public void Procesando_no_pisa_un_incompleto()
    {
        var charge = NewCharge();
        charge.MarkPartial();

        Assert.False(charge.MarkProcessing());
        Assert.Equal(SubscriptionChargeStatus.Partial, charge.Status);
    }

    [Theory]
    [InlineData(199, true)]
    [InlineData(200, true)]
    [InlineData(198.99, false)]
    public void Se_cubre_con_la_suma_de_la_base(decimal paid, bool covered)
    {
        Assert.Equal(covered, NewCharge().IsCoveredBy(paid));
    }

    [Fact]
    public void Se_reutiliza_solo_pendiente_con_link_del_mismo_paquete_y_monto()
    {
        var quote = SubscriptionPricing.Quote(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, Settings);
        var now = Mx(2026, 11, 20, 10);
        var charge = SubscriptionCharge.Create(7, quote, false, SubscriptionChargeOrigin.Merchant, now);
        Assert.False(charge.CanBeReusedFor(quote, now, 5)); // sin link
        charge.AttachLink("01PL", "https://pay.pey.mx/01PL");

        Assert.True(charge.CanBeReusedFor(quote, now, 5));
        Assert.False(charge.CanBeReusedFor(SubscriptionPricing.Quote(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, Settings, isFounder: true), now, 5));
        Assert.False(charge.CanBeReusedFor(SubscriptionPricing.Quote(SubscriptionPlan.Pro, SubscriptionPackage.Annual, Settings), now, 5));
    }

    [Fact]
    public void El_cargo_guarda_el_plan_y_no_se_reutiliza_para_otro_plan()
    {
        var basic = SubscriptionPricing.Quote(SubscriptionPlan.Basic, SubscriptionPackage.OneMonth, Settings);
        var now = Mx(2026, 11, 20, 10);
        var charge = SubscriptionCharge.Create(7, basic, false, SubscriptionChargeOrigin.Merchant, now);
        charge.AttachLink("01PL", "https://pay.pey.mx/01PL");

        Assert.Equal(SubscriptionPlan.Basic, charge.Plan);
        Assert.Equal(99m, charge.Amount);
        Assert.Null(charge.TermsVersion);
        Assert.True(charge.CanBeReusedFor(basic, now, 5));
        Assert.False(charge.CanBeReusedFor(basic with { Plan = SubscriptionPlan.Pro }, now, 5)); // mismo monto, otro plan
    }

    [Fact]
    public void Se_reutiliza_solo_si_tiene_a_lo_mas_los_dias_de_gracia()
    {
        var quote = SubscriptionPricing.Quote(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, Settings);
        var created = Mx(2026, 11, 20, 10);
        var charge = SubscriptionCharge.Create(7, quote, false, SubscriptionChargeOrigin.Merchant, created);
        charge.AttachLink("01PL", "https://pay.pey.mx/01PL");

        Assert.True(charge.CanBeReusedFor(quote, created.AddDays(5), 5));
        Assert.False(charge.CanBeReusedFor(quote, created.AddDays(5).AddMinutes(1), 5));
        Assert.False(charge.CanBeReusedFor(quote, created.AddMinutes(1), 0)); // sin días de gracia no se reutiliza
    }

    // --- Cotización por vendedor y periodo pagado ------------------------------------------------------------------------

    [Fact]
    public void Con_lugar_de_fundador_cotiza_precio_de_fundador()
    {
        var subscription = Subscription.StartTrial(7, Mx(2026, 11, 1, 10), 30);
        var status = subscription.StatusAt(Mx(2026, 11, 20, 10), GraceDays);

        var (withSlot, founder) = subscription.QuoteFor(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, Settings, status, founderSlotAvailable: true);
        var (withoutSlot, normal) = subscription.QuoteFor(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, Settings, status, founderSlotAvailable: false);

        Assert.True(founder);
        Assert.Equal(149m, withSlot.Amount);
        Assert.False(normal);
        Assert.Equal(199m, withoutSlot.Amount);
    }

    [Fact]
    public void El_fundador_conserva_su_precio_aunque_ya_no_haya_lugares()
    {
        var subscription = Subscription.StartTrial(7, Mx(2026, 11, 1, 10), 30);
        subscription.AssignFounder(3);
        var status = subscription.StatusAt(Mx(2026, 11, 20, 10), GraceDays);

        var (quote, founder) = subscription.QuoteFor(SubscriptionPlan.Pro, SubscriptionPackage.Annual, Settings, status, founderSlotAvailable: false);

        Assert.True(founder);
        Assert.Equal(1490m, quote.Amount);
        Assert.Throws<InvalidOperationException>(() => subscription.AssignFounder(4));
    }

    [Fact]
    public void El_anual_congelado_solo_aplica_sin_bajar_al_gratuito()
    {
        var subscription = Subscription.StartTrial(7, Mx(2026, 1, 1, 10), 30);
        subscription.AddPaidPeriod(SubscriptionPlan.Pro, SubscriptionPackage.Annual, Mx(2026, 1, 15, 10), GraceDays, subscriptionChargeId: 1);
        subscription.LockAnnualPrice(SubscriptionPlan.Pro, 1990m);
        var raised = new SystemSettings { ProAnnualPrice = 2390m };

        var inPro = subscription.QuoteFor(SubscriptionPlan.Pro, SubscriptionPackage.Annual, raised, subscription.StatusAt(Mx(2026, 12, 20, 10), GraceDays), false);
        var inFree = subscription.QuoteFor(SubscriptionPlan.Pro, SubscriptionPackage.Annual, raised, subscription.StatusAt(Mx(2027, 6, 1, 10), GraceDays), false);

        Assert.Equal(1990m, inPro.Quote.Amount);
        Assert.Equal(2390m, inFree.Quote.Amount);
    }

    [Fact]
    public void El_anual_congelado_es_del_plan_que_se_pago()
    {
        var subscription = Subscription.StartTrial(7, Mx(2026, 1, 1, 10), 30);
        subscription.AddPaidPeriod(SubscriptionPlan.Basic, SubscriptionPackage.Annual, Mx(2026, 1, 15, 10), GraceDays, subscriptionChargeId: 1);
        subscription.LockAnnualPrice(SubscriptionPlan.Basic, 990m);
        var raised = new SystemSettings { BasicAnnualPrice = 1190m, ProAnnualPrice = 2390m };
        var status = subscription.StatusAt(Mx(2026, 12, 20, 10), GraceDays);

        Assert.Equal(SubscriptionPlan.Basic, subscription.LockedAnnualPlan);
        Assert.Equal(990m, subscription.QuoteFor(SubscriptionPlan.Basic, SubscriptionPackage.Annual, raised, status, false).Quote.Amount);
        Assert.Equal(2390m, subscription.QuoteFor(SubscriptionPlan.Pro, SubscriptionPackage.Annual, raised, status, false).Quote.Amount);
    }

    [Fact]
    public void Pagar_otro_plan_suelta_el_anual_congelado()
    {
        var subscription = Subscription.StartTrial(7, Mx(2026, 1, 1, 10), 30);
        subscription.AddPaidPeriod(SubscriptionPlan.Basic, SubscriptionPackage.Annual, Mx(2026, 1, 15, 10), GraceDays, subscriptionChargeId: 1);
        subscription.LockAnnualPrice(SubscriptionPlan.Basic, 990m);

        subscription.AddPaidPeriod(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, Mx(2026, 2, 1, 10), GraceDays, subscriptionChargeId: 2);

        Assert.Null(subscription.LockedAnnualPrice);
        Assert.Null(subscription.LockedAnnualPlan);
        Assert.Equal(SubscriptionPlan.Pro, subscription.LastPlan);
        Assert.Equal(SubscriptionPackage.OneMonth, subscription.LastPackage);
    }

    [Fact]
    public void Renovar_el_mismo_plan_conserva_el_anual_congelado()
    {
        var subscription = Subscription.StartTrial(7, Mx(2026, 1, 1, 10), 30);
        subscription.AddPaidPeriod(SubscriptionPlan.Pro, SubscriptionPackage.Annual, Mx(2026, 1, 15, 10), GraceDays, subscriptionChargeId: 1);
        subscription.LockAnnualPrice(SubscriptionPlan.Pro, 1990m);

        subscription.AddPaidPeriod(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, Mx(2026, 6, 1, 10), GraceDays, subscriptionChargeId: 2);

        Assert.Equal(1990m, subscription.LockedAnnualPrice);
        Assert.Equal(SubscriptionPlan.Pro, subscription.LockedAnnualPlan);
    }

    [Fact]
    public void El_periodo_pagado_guarda_su_cargo()
    {
        var subscription = Subscription.StartTrial(7, Mx(2026, 11, 1, 10), 30);

        var period = subscription.AddPaidPeriod(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, Mx(2026, 11, 20, 10), GraceDays, subscriptionChargeId: 42);

        Assert.Equal(42, period.SubscriptionChargeId);
        Assert.Equal(Mx(2026, 12, 1, 10), period.StartsAt);
    }

    // --- Ofertas -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Las_ofertas_traen_equivalencias_y_fecha_de_Pro()
    {
        var subscription = Subscription.StartTrial(7, Mx(2026, 11, 1, 10), 30);

        var offers = SubscriptionOffers.For(subscription, SubscriptionPlan.Pro, Settings, Mx(2026, 11, 20, 10), founderSlotAvailable: false);

        Assert.Equal(Enum.GetValues<SubscriptionPackage>(), offers.Select(o => o.Package));
        var six = offers.Single(o => o.Package == SubscriptionPackage.SixMonths);
        var annual = offers.Single(o => o.Package == SubscriptionPackage.Annual);
        var month = offers.Single(o => o.Package == SubscriptionPackage.OneMonth);
        Assert.Equal(796m, six.AnnualUpgradeDifference);
        Assert.Equal(2, annual.GiftMonths);
        Assert.Equal(165.83m, annual.MonthlyEquivalent);
        Assert.Null(month.AnnualUpgradeDifference);
        Assert.Equal(new DateOnly(2027, 1, 1), month.LastDay); // la prueba acaba el 1/12 a las 10:00; + 1 mes
    }

    [Fact]
    public void Las_ofertas_de_fundador_usan_sus_precios()
    {
        var subscription = Subscription.StartTrial(7, Mx(2026, 11, 1, 10), 30);

        var offers = SubscriptionOffers.For(subscription, SubscriptionPlan.Pro, Settings, Mx(2026, 11, 20, 10), founderSlotAvailable: true);

        Assert.All(offers, o => Assert.True(o.IsFounderPrice));
        Assert.Equal(596m, offers.Single(o => o.Package == SubscriptionPackage.SixMonths).AnnualUpgradeDifference);
        Assert.Equal(124.17m, offers.Single(o => o.Package == SubscriptionPackage.Annual).MonthlyEquivalent);
    }

    // --- Calendario de conciliación ------------------------------------------------------------------------------------

    [Theory]
    [InlineData(5, null, false)]          // recién creado: le toca al webhook
    [InlineData(15, null, true)]          // primera consulta
    [InlineData(60, 5, false)]            // cada 10 min las primeras 2 h
    [InlineData(60, 11, true)]
    [InlineData(5 * 60, 30, false)]       // cada hora hasta 3 días
    [InlineData(5 * 60, 61, true)]
    [InlineData(5 * 24 * 60, 12 * 60, false)] // diario hasta 60 días
    [InlineData(5 * 24 * 60, 25 * 60, true)]
    [InlineData(61 * 24 * 60, null, false)]   // más de 60 días: queda para el admin
    public void Calendario_de_la_conciliacion(int createdMinutesAgo, int? checkedMinutesAgo, bool due)
    {
        var now = Mx(2026, 11, 20, 12);
        var charge = NewCharge();
        charge.AttachLink("01PL", "https://pay.pey.mx/01PL");
        charge.CreatedAt = now.AddMinutes(-createdMinutesAgo);
        charge.PaymentCheckedAt = checkedMinutesAgo is { } m ? now.AddMinutes(-m) : null;

        Assert.Equal(due, PaymentLinkReconciliationSchedule.IsDue(charge, now));
    }

    [Theory]
    [InlineData(SubscriptionChargeStatus.Pending, true)]
    [InlineData(SubscriptionChargeStatus.Processing, true)]
    [InlineData(SubscriptionChargeStatus.Partial, true)]
    [InlineData(SubscriptionChargeStatus.Paid, false)]
    [InlineData(SubscriptionChargeStatus.Failed, false)]
    public void Solo_se_concilian_cargos_sin_abonar_con_link(SubscriptionChargeStatus status, bool due)
    {
        var now = Mx(2026, 11, 20, 12);
        var charge = NewCharge();
        charge.AttachLink("01PL", "https://pay.pey.mx/01PL");
        charge.CreatedAt = now.AddMinutes(-30);
        charge.Status = status;

        Assert.Equal(due, PaymentLinkReconciliationSchedule.IsDue(charge, now));
        var withoutLink = NewCharge();
        withoutLink.CreatedAt = now.AddMinutes(-30);
        Assert.False(PaymentLinkReconciliationSchedule.IsDue(withoutLink, now));
    }

    private static SubscriptionCharge NewCharge() => SubscriptionCharge.Create(
        7, SubscriptionPricing.Quote(SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, Settings), false, SubscriptionChargeOrigin.Merchant,
        Mx(2026, 11, 20, 10));
}
