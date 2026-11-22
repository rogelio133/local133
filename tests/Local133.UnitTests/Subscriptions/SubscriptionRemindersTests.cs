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

    private static SubscriptionReminder? Monthly(int month, int day, int hour = 9) =>
        At(Mx(2026, month, day, hour), SubscriptionPeriodKind.Paid, SubscriptionPackage.OneMonth, Trial, PaidMonth);

    [Fact]
    public void En_el_onboarding_no_hay_recordatorios()
    {
        Assert.Null(SubscriptionReminders.For(PlanStatus.Onboarding, SubscriptionPeriodKind.Trial, null, Mx(2026, 12, 1, 9)));
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
        Assert.Equal(new DateOnly(2026, 12, 20), reminder.LastProDay);
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

        Assert.Equal(SubscriptionReminderStage.Downgraded, reminder.Stage);
        Assert.Equal(NotificationType.DowngradedToFree, reminder.NotificationType);
    }

    [Fact]
    public void La_baja_no_se_avisa_despues_de_3_dias()
    {
        Assert.Null(Monthly(12, 28));
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
    [InlineData(20, SubscriptionReminderStage.Downgraded, 0)] // baja directo, sin gracia
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
                expected == SubscriptionReminderStage.Upcoming ? NotificationType.TrialEnding : NotificationType.DowngradedToFree,
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
        Assert.Equal(NotificationType.ProDaysEnding, upcoming.NotificationType);
        Assert.Equal(SubscriptionReminderStage.Downgraded, downgraded.Stage);
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
        Assert.Equal(new DateOnly(2026, 12, 20), reminder.LastProDay);
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
    public void Despues_de_la_baja_no_hay_aviso_de_renovar()
    {
        Assert.NotNull(Monthly(12, 26)); // el correo de la baja sí sale
        Assert.Null(Notice(Mx(2026, 12, 26, 9)));
    }

    private static SubscriptionReminder? Notice(DateTimeOffset now)
    {
        var list = new[] { Trial, PaidMonth }
            .Select(p => new SubscriptionPeriod { MerchantId = 1, Kind = p.Item1, StartsAt = p.Item2, EndsAt = p.Item3 }).ToList();
        return SubscriptionReminders.RenewalNoticeFor(
            PlanStatus.For(list, now, GraceDays), SubscriptionPeriodKind.Paid, SubscriptionPackage.OneMonth, now);
    }

    private static SubscriptionReminder? At(
        DateTimeOffset now, SubscriptionPeriodKind lastKind, SubscriptionPackage? package,
        params (SubscriptionPeriodKind Kind, DateTimeOffset Start, DateTimeOffset End)[] periods)
    {
        var list = periods.Select(p => new SubscriptionPeriod { MerchantId = 1, Kind = p.Kind, StartsAt = p.Start, EndsAt = p.End }).ToList();
        return SubscriptionReminders.For(PlanStatus.For(list, now, GraceDays), lastKind, package, now);
    }
}
