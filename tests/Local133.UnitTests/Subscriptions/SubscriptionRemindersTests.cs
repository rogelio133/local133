using Local133.Domain.Notifications;
using Local133.Domain.Subscriptions;
using static Local133.UnitTests.Subscriptions.PlanStatusTests;

namespace Local133.UnitTests.Subscriptions;

/// <summary>
/// Calendario de los recordatorios de la suscripción (5.7), con el job a las 9:00 de México en cada borde. México va
/// en UTC−6, sin horario de verano.
/// </summary>
public class SubscriptionRemindersTests
{
    private const int GraceDays = 5;

    private static readonly (SubscriptionPeriodKind, DateTimeOffset, DateTimeOffset) Trial =
        (SubscriptionPeriodKind.Trial, Mx(2026, 10, 20, 15), Mx(2026, 11, 20, 15));

    /// <summary>Mes pagado tras la prueba: vence el 20 de diciembre a las 15:00, gracia hasta el 25 a las 15:00.</summary>
    private static readonly (SubscriptionPeriodKind, DateTimeOffset, DateTimeOffset) PaidMonth =
        (SubscriptionPeriodKind.Paid, Mx(2026, 11, 20, 15), Mx(2026, 12, 20, 15));

    private static SubscriptionReminder? Monthly(int month, int day, int hour = 9, int year = 2026) =>
        At(Mx(year, month, day, hour), SubscriptionPeriodKind.Paid, SubscriptionPackage.OneMonth, Trial, PaidMonth);

    [Fact]
    public void En_el_onboarding_no_hay_recordatorios()
    {
        Assert.Null(SubscriptionReminders.For(PlanStatus.Onboarding, SubscriptionPeriodKind.Trial, SubscriptionPlan.Pro, null, Mx(2026, 12, 1, 9)));
    }

    [Theory]
    [InlineData(12, 5)]  // 15 días antes: solo el anual
    [InlineData(12, 16)] // 4 días antes
    public void El_mensual_no_avisa_antes_de_3_dias(int month, int day)
    {
        Assert.Null(Monthly(month, day));
    }

    [Theory]
    [InlineData(17, 3)]
    [InlineData(18, 2)]
    [InlineData(19, 1)]
    public void El_mensual_avisa_desde_3_dias_antes_con_el_link_del_mismo_paquete(int day, int daysLeft)
    {
        var reminder = Monthly(12, day)!;

        Assert.Equal(SubscriptionReminderStage.Upcoming, reminder.Stage);
        Assert.Equal(daysLeft, reminder.DaysLeft);
        Assert.Equal(NotificationType.SubscriptionUpcoming, reminder.NotificationType);
        Assert.Equal(new DateOnly(2026, 12, 20), reminder.LastActiveDay);
        Assert.Equal(new DateOnly(2026, 12, 25), reminder.LastGraceDay);
    }

    [Fact]
    public void El_dia_del_vencimiento_avisa_que_vence_hoy()
    {
        var reminder = Monthly(12, 20)!;

        Assert.Equal(SubscriptionReminderStage.DueToday, reminder.Stage);
        Assert.Equal(0, reminder.DaysLeft);
        Assert.Equal(NotificationType.SubscriptionDueToday, reminder.NotificationType);
    }

    [Fact]
    public void Si_vencio_hoy_antes_de_las_9_ya_esta_en_gracia_pero_el_aviso_es_el_de_vence_hoy()
    {
        var reminder = At(Mx(2026, 12, 20, 9), SubscriptionPeriodKind.Paid, SubscriptionPackage.OneMonth,
            Trial, (SubscriptionPeriodKind.Paid, Mx(2026, 11, 20, 5), Mx(2026, 12, 20, 5)))!;

        Assert.Equal(SubscriptionReminderStage.DueToday, reminder.Stage);
    }

    [Theory]
    [InlineData(21)] // día 1 de la gracia
    [InlineData(22)] // día 2
    public void Los_dias_1_y_2_de_la_gracia_no_avisan(int day)
    {
        Assert.Null(Monthly(12, day));
    }

    [Theory]
    [InlineData(23, 2)] // día 3 de la gracia
    [InlineData(24, 1)]
    [InlineData(25, 0)] // último día de la gracia (termina a las 15:00)
    public void Desde_el_dia_3_de_la_gracia_avisa_cuantos_dias_quedan_para_pagar(int day, int daysLeft)
    {
        var reminder = Monthly(12, day)!;

        Assert.Equal(SubscriptionReminderStage.Grace, reminder.Stage);
        Assert.Equal(daysLeft, reminder.DaysLeft);
        Assert.Equal(NotificationType.SubscriptionGrace, reminder.NotificationType);
    }

    [Theory]
    [InlineData(26)]
    [InlineData(27)]
    public void Al_terminar_la_gracia_avisa_la_baja_sin_link(int day)
    {
        var reminder = Monthly(12, day)!;

        Assert.Equal(SubscriptionReminderStage.Expired, reminder.Stage);
        Assert.Equal(NotificationType.PlanExpired, reminder.NotificationType);
    }

    [Fact]
    public void El_vencido_no_se_avisa_del_dia_3_al_6()
    {
        Assert.Null(Monthly(12, 28)); // día 3 desde el fin de la gracia (25 a las 15:00)
        Assert.Null(Monthly(12, 31)); // día 6
    }

    [Theory]
    [InlineData(1)] // día 7 desde el fin de la gracia
    [InlineData(2)] // día 8: si el job no corrió el 7
    [InlineData(3)] // día 9
    public void El_seguimiento_del_vencido_sale_del_dia_7_al_9(int day)
    {
        var reminder = Monthly(1, day, year: 2027)!;

        Assert.Equal(SubscriptionReminderStage.ExpiredFollowUp, reminder.Stage);
        Assert.Equal(NotificationType.PlanExpiredFollowUp, reminder.NotificationType);
        Assert.Equal(0, reminder.DaysLeft);
        Assert.StartsWith("sub:ExpiredFollowUp:", reminder.DedupKey, StringComparison.Ordinal);
    }

    [Fact]
    public void Despues_del_seguimiento_no_hay_mas_recordatorios()
    {
        Assert.Null(Monthly(1, 4, year: 2027));  // día 10
        Assert.Null(Monthly(1, 20, year: 2027));
    }

    [Theory]
    [InlineData(SubscriptionPeriodKind.Trial)]
    [InlineData(SubscriptionPeriodKind.ReferralReward)]
    [InlineData(SubscriptionPeriodKind.Conversion)]
    [InlineData(SubscriptionPeriodKind.Transition)]
    public void Sin_gracia_el_vencido_y_su_seguimiento_cuentan_desde_el_fin_del_periodo(SubscriptionPeriodKind kind)
    {
        // Termina el 20 de diciembre a las 15:00: vencido desde ese momento.
        var plan = kind is SubscriptionPeriodKind.Trial or SubscriptionPeriodKind.Conversion ? SubscriptionPlan.Pro : SubscriptionPlan.Basic;
        var periods = new[]
        {
            (SubscriptionPeriodKind.Trial, Mx(2026, 11, 20, 15), Mx(2026, 12, 5, 15), SubscriptionPlan.Pro),
            (kind, Mx(2026, 12, 5, 15), Mx(2026, 12, 20, 15), plan),
        };
        if (kind == SubscriptionPeriodKind.Trial)
        {
            periods = [(SubscriptionPeriodKind.Trial, Mx(2026, 12, 5, 15), Mx(2026, 12, 20, 15), SubscriptionPlan.Pro)];
        }

        SubscriptionReminder? On(int day) => AtPlans(Mx(2026, 12, day, 9), kind, null, periods);

        Assert.Equal(SubscriptionReminderStage.Upcoming, On(20)!.Stage); // último día: sin aviso «vence hoy»
        Assert.Equal(SubscriptionReminderStage.Expired, On(21)!.Stage);
        Assert.Equal(SubscriptionReminderStage.Expired, On(22)!.Stage);
        Assert.Null(On(23));
        Assert.Null(On(26));
        var followUp = On(27)!;
        Assert.Equal(SubscriptionReminderStage.ExpiredFollowUp, followUp.Stage);
        Assert.Equal(plan, followUp.EndingPlan);
        Assert.Equal(kind, followUp.EndingKind);
        Assert.Null(On(30));
    }

    [Fact]
    public void El_recordatorio_lleva_el_plan_que_vence()
    {
        var basic = AtPlans(Mx(2026, 12, 17, 9), SubscriptionPeriodKind.Paid, SubscriptionPackage.OneMonth,
            (SubscriptionPeriodKind.Trial, Mx(2026, 10, 20, 15), Mx(2026, 11, 20, 15), SubscriptionPlan.Pro),
            (SubscriptionPeriodKind.Paid, Mx(2026, 11, 20, 15), Mx(2026, 12, 20, 15), SubscriptionPlan.Basic))!;
        var pro = Monthly(12, 17)!;

        Assert.Equal(SubscriptionPlan.Basic, basic.EndingPlan);
        Assert.Equal(SubscriptionPlan.Pro, pro.EndingPlan);
    }

    [Fact]
    public void Pro_seguido_de_Basico_avisa_hasta_que_vence_Basico()
    {
        // Pro hasta el 20 de diciembre, después Básico hasta el 20 de enero: el 17 de diciembre no vence nada.
        var periods = new[]
        {
            (SubscriptionPeriodKind.Trial, Mx(2026, 10, 20, 15), Mx(2026, 11, 20, 15), SubscriptionPlan.Pro),
            (SubscriptionPeriodKind.Paid, Mx(2026, 11, 20, 15), Mx(2026, 12, 20, 15), SubscriptionPlan.Pro),
            (SubscriptionPeriodKind.Paid, Mx(2026, 12, 20, 15), Mx(2027, 1, 20, 15), SubscriptionPlan.Basic),
        };

        Assert.Null(AtPlans(Mx(2026, 12, 17, 9), SubscriptionPeriodKind.Paid, SubscriptionPackage.OneMonth, periods));
        var reminder = AtPlans(Mx(2027, 1, 17, 9), SubscriptionPeriodKind.Paid, SubscriptionPackage.OneMonth, periods)!;
        Assert.Equal(SubscriptionReminderStage.Upcoming, reminder.Stage);
        Assert.Equal(SubscriptionPlan.Basic, reminder.EndingPlan);
    }

    [Theory]
    [InlineData(4, null)]                                         // 16 días antes
    [InlineData(5, SubscriptionReminderStage.AnnualUpcoming)]     // 15 días antes
    [InlineData(16, SubscriptionReminderStage.AnnualUpcoming)]    // 4 días antes
    [InlineData(17, SubscriptionReminderStage.Upcoming)]          // 3 días antes
    public void El_anual_avisa_tambien_15_dias_antes(int day, SubscriptionReminderStage? expected)
    {
        var reminder = At(Mx(2027, 11, day, 9), SubscriptionPeriodKind.Paid, SubscriptionPackage.Annual,
            Trial, (SubscriptionPeriodKind.Paid, Mx(2026, 11, 20, 15), Mx(2027, 11, 20, 15)));

        Assert.Equal(expected, reminder?.Stage);
        if (reminder is not null)
        {
            Assert.Equal(20 - day, reminder.DaysLeft);
        }
    }

    [Theory]
    [InlineData(15, null, 0)]
    [InlineData(16, SubscriptionReminderStage.Upcoming, 3)]
    [InlineData(19, SubscriptionReminderStage.Upcoming, 0)] // la prueba no tiene aviso «vence hoy»: sigue el de 3 días
    [InlineData(20, SubscriptionReminderStage.Expired, 0)] // baja directo, sin gracia
    public void La_prueba_avisa_3_dias_antes_y_al_bajar_al_gratuito_sin_link(int day, SubscriptionReminderStage? expected, int daysLeft)
    {
        var reminder = At(Mx(2026, 12, day, 9), SubscriptionPeriodKind.Trial, null,
            (SubscriptionPeriodKind.Trial, Mx(2026, 11, 19, 15), Mx(2026, 12, 19, 15)));

        Assert.Equal(expected, reminder?.Stage);
        if (reminder is not null)
        {
            Assert.Equal(daysLeft, reminder.DaysLeft);
            Assert.Null(reminder.LastGraceDay);
            Assert.Equal(
                expected == SubscriptionReminderStage.Upcoming ? NotificationType.TrialEnding : NotificationType.PlanExpired,
                reminder.NotificationType);
        }
    }

    [Fact]
    public void La_prueba_con_un_pago_por_delante_no_avisa_que_termina()
    {
        var reminder = At(Mx(2026, 12, 16, 9), SubscriptionPeriodKind.Paid, SubscriptionPackage.OneMonth,
            (SubscriptionPeriodKind.Trial, Mx(2026, 11, 19, 15), Mx(2026, 12, 19, 15)),
            (SubscriptionPeriodKind.Paid, Mx(2026, 12, 19, 15), Mx(2027, 1, 19, 15)));

        Assert.Null(reminder);
    }

    [Fact]
    public void Los_dias_regalados_avisan_que_terminan_y_bajan_sin_gracia()
    {
        var reward = (SubscriptionPeriodKind.ReferralReward, Mx(2026, 12, 20, 15), Mx(2026, 12, 30, 15));

        var upcoming = At(Mx(2026, 12, 27, 9), SubscriptionPeriodKind.ReferralReward, SubscriptionPackage.OneMonth, Trial, PaidMonth, reward)!;
        var downgraded = At(Mx(2026, 12, 31, 9), SubscriptionPeriodKind.ReferralReward, SubscriptionPackage.OneMonth, Trial, PaidMonth, reward)!;

        Assert.Equal(SubscriptionReminderStage.Upcoming, upcoming.Stage);
        Assert.Equal(NotificationType.PlanDaysEnding, upcoming.NotificationType);
        Assert.Equal(SubscriptionReminderStage.Expired, downgraded.Stage);
    }

    [Fact]
    public void Medianoche_de_Mexico_y_no_de_UTC()
    {
        // Vence a la medianoche del 21 en México (06:00 UTC del 21): su último día es el 20.
        var paid = (SubscriptionPeriodKind.Paid, Mx(2026, 11, 21, 0), Mx(2026, 12, 21, 0));
        // 20 de diciembre a las 21:00 en México = 21 de diciembre a las 03:00 UTC.
        var evening = Mx(2026, 12, 20, 21);
        Assert.Equal(21, evening.UtcDateTime.Day);

        var reminder = At(evening, SubscriptionPeriodKind.Paid, SubscriptionPackage.OneMonth, Trial, paid)!;

        Assert.Equal(SubscriptionReminderStage.DueToday, reminder.Stage);
        Assert.Equal(new DateOnly(2026, 12, 20), reminder.LastActiveDay);
    }

    [Fact]
    public void La_llave_cambia_con_la_etapa_y_con_el_fin_del_plan()
    {
        var upcoming = Monthly(12, 17)!;
        var dueToday = Monthly(12, 20)!;
        var renewed = At(Mx(2027, 1, 17, 9), SubscriptionPeriodKind.Paid, SubscriptionPackage.OneMonth,
            Trial, PaidMonth, (SubscriptionPeriodKind.Paid, Mx(2026, 12, 20, 15), Mx(2027, 1, 20, 15)))!;

        Assert.Equal("sub:Upcoming:20261220210000", upcoming.DedupKey);
        Assert.NotEqual(upcoming.DedupKey, dueToday.DedupKey);
        Assert.Equal(SubscriptionReminderStage.Upcoming, renewed.Stage);
        Assert.NotEqual(upcoming.DedupKey, renewed.DedupKey);
        Assert.Equal(Monthly(12, 18)!.DedupKey, upcoming.DedupKey); // misma etapa, mismo ciclo: un solo aviso
    }

    // --- Aviso de «Mi plan» --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(21)] // día 1 de la gracia: el correo todavía no sale, el aviso sí
    [InlineData(22)] // día 2
    [InlineData(25)] // último día de la gracia
    public void El_aviso_de_Mi_plan_cubre_toda_la_gracia(int day)
    {
        var notice = Notice(Mx(2026, 12, day, 9));

        Assert.NotNull(notice);
        Assert.Equal(SubscriptionReminderStage.Grace, notice.Stage);
        Assert.Equal(new DateOnly(2026, 12, 25), notice.LastGraceDay);
    }

    [Fact]
    public void El_aviso_de_Mi_plan_sigue_las_ventanas_antes_de_vencer()
    {
        Assert.Null(Notice(Mx(2026, 12, 16, 9)));                                       // 4 días antes
        Assert.Equal(SubscriptionReminderStage.Upcoming, Notice(Mx(2026, 12, 17, 9))!.Stage);
        Assert.Equal(SubscriptionReminderStage.DueToday, Notice(Mx(2026, 12, 20, 9))!.Stage);
    }

    [Fact]
    public void Con_el_plan_vencido_no_hay_aviso_de_renovar()
    {
        Assert.NotNull(Monthly(12, 26)); // el correo del vencido sí sale
        Assert.Null(Notice(Mx(2026, 12, 26, 9)));
        Assert.NotNull(Monthly(1, 1, year: 2027)); // y el del seguimiento
        Assert.Null(Notice(Mx(2027, 1, 1, 9)));
    }

    private static SubscriptionReminder? Notice(DateTimeOffset now)
    {
        var list = new[] { Trial, PaidMonth }
            .Select(p => new SubscriptionPeriod { MerchantId = 1, Kind = p.Item1, StartsAt = p.Item2, EndsAt = p.Item3 }).ToList();
        return SubscriptionReminders.RenewalNoticeFor(
            PlanStatus.For(list, now, GraceDays), SubscriptionPeriodKind.Paid, SubscriptionPlan.Pro, SubscriptionPackage.OneMonth, now);
    }

    private static SubscriptionReminder? At(
        DateTimeOffset now, SubscriptionPeriodKind lastKind, SubscriptionPackage? package,
        params (SubscriptionPeriodKind Kind, DateTimeOffset Start, DateTimeOffset End)[] periods) =>
        AtPlans(now, lastKind, package, periods.Select(p => (p.Kind, p.Start, p.End, SubscriptionPlan.Pro)).ToArray());

    private static SubscriptionReminder? AtPlans(
        DateTimeOffset now, SubscriptionPeriodKind lastKind, SubscriptionPackage? package,
        params (SubscriptionPeriodKind Kind, DateTimeOffset Start, DateTimeOffset End, SubscriptionPlan Plan)[] periods)
    {
        var list = periods
            .Select(p => new SubscriptionPeriod { MerchantId = 1, Kind = p.Kind, Plan = p.Plan, StartsAt = p.Start, EndsAt = p.End })
            .ToList();
        var endingPlan = list.MaxBy(p => p.EndsAt)!.Plan;
        return SubscriptionReminders.For(PlanStatus.For(list, now, GraceDays), lastKind, endingPlan, package, now);
    }
}
