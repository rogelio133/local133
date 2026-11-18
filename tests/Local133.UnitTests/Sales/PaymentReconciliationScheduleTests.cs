using Local133.Domain.Sales;

namespace Local133.UnitTests.Sales;

/// <summary>Calendario de la conciliación de pagos (paso 2.9): cuándo se le pregunta a Pey por una venta sin pagar.</summary>
public class PaymentReconciliationScheduleTests
{
    private static readonly DateTimeOffset Created = new(2026, 11, 18, 18, 0, 0, TimeSpan.Zero);

    /// <summary>Venta con QR de 10 minutos (vence a las 18:10).</summary>
    private static Sale QrSale(DateTimeOffset? checkedAt = null)
    {
        var sale = Sale.StartDirect(7, 1, 150m, "Tacos", Created.AddMinutes(10), Created);
        sale.AttachQr("IDC123", "{}");
        sale.PaymentCheckedAt = checkedAt;
        return sale;
    }

    private static Sale PushSale(string? transactionId)
    {
        var sale = Sale.StartPush(7, 1, 150m, "Tacos", "5512345678", Created.AddMinutes(10), Created);
        if (transactionId is not null)
        {
            sale.AttachPushNotification(transactionId, "12345678901234567890");
        }

        return sale;
    }

    private static DateTimeOffset At(double minutesAfterCreation) => Created.AddMinutes(minutesAfterCreation);

    [Fact]
    public void Recien_creada_no_se_consulta_para_darle_tiempo_al_webhook()
    {
        Assert.False(PaymentReconciliationSchedule.IsDue(QrSale(), At(1.9)));
        Assert.True(PaymentReconciliationSchedule.IsDue(QrSale(), At(2)));
    }

    [Fact]
    public void Vigente_se_vuelve_a_consultar_al_cumplirse_el_intervalo()
    {
        Assert.False(PaymentReconciliationSchedule.IsDue(QrSale(checkedAt: At(2)), At(4.9)));
        Assert.True(PaymentReconciliationSchedule.IsDue(QrSale(checkedAt: At(2)), At(5)));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(19.9)]
    public void Ya_vencida_espera_a_la_consulta_final(double minutes)
    {
        Assert.False(PaymentReconciliationSchedule.IsDue(QrSale(checkedAt: At(8)), At(minutes)));
    }

    [Fact]
    public void La_consulta_final_es_diez_minutos_despues_del_vencimiento()
    {
        Assert.True(PaymentReconciliationSchedule.IsDue(QrSale(checkedAt: At(8)), At(20)));
    }

    [Fact]
    public void Hecha_la_consulta_final_ya_no_se_consulta()
    {
        Assert.False(PaymentReconciliationSchedule.IsDue(QrSale(checkedAt: At(20)), At(21)));
        Assert.False(PaymentReconciliationSchedule.IsDue(QrSale(checkedAt: At(25)), At(60 * 5)));
    }

    [Fact]
    public void Si_nunca_se_consulto_la_consulta_final_alcanza_hasta_24_horas_despues_del_vencimiento()
    {
        Assert.True(PaymentReconciliationSchedule.IsDue(QrSale(), At(10 + 60 * 24 - 1)));
        Assert.False(PaymentReconciliationSchedule.IsDue(QrSale(), At(10 + 60 * 24)));
    }

    [Fact]
    public void Pagada_no_se_consulta()
    {
        var sale = QrSale();
        sale.MarkPaid(At(1));

        Assert.False(PaymentReconciliationSchedule.IsDue(sale, At(3)));
        Assert.False(PaymentReconciliationSchedule.IsDue(sale, At(20)));
    }

    [Theory]
    [InlineData(SaleStatus.Cancelled)]
    [InlineData(SaleStatus.Expired)]
    public void Cancelada_o_vencida_se_consulta_igual_porque_el_QR_seguia_cobrable(SaleStatus status)
    {
        var sale = QrSale(checkedAt: At(8));
        sale.Status = status;

        Assert.True(PaymentReconciliationSchedule.IsDue(sale, At(20)));
    }

    [Fact]
    public void Sin_QR_no_hay_que_consultar()
    {
        var sale = Sale.StartDirect(7, 1, 150m, "Tacos", Created.AddMinutes(10), Created);

        Assert.False(PaymentReconciliationSchedule.IsDue(sale, At(3)));
    }

    [Fact]
    public void Cobro_por_celular_se_consulta_por_su_transaccion()
    {
        Assert.True(PaymentReconciliationSchedule.IsDue(PushSale("01TXFAKE"), At(3)));
    }

    [Fact]
    public void Cobro_por_celular_sin_transaccion_confirmada_no_se_consulta()
    {
        Assert.False(PaymentReconciliationSchedule.IsDue(PushSale(transactionId: null), At(3)));
        Assert.False(PaymentReconciliationSchedule.IsDue(PushSale(transactionId: null), At(20)));
    }

    [Fact]
    public void Sin_vencimiento_no_se_consulta()
    {
        var sale = QrSale();
        sale.ExpiresAt = null;

        Assert.False(PaymentReconciliationSchedule.IsDue(sale, At(3)));
    }
}
