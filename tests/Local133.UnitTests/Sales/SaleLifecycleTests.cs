using Local133.Domain.Sales;

namespace Local133.UnitTests.Sales;

/// <summary>Transiciones de la venta: pago (el pago manda), cancelación y expiración.</summary>
public class SaleLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 18, 0, 0, TimeSpan.Zero);

    private static Sale Pending(DateTimeOffset? expiresAt = null) =>
        Sale.StartDirect(7, 1, 150m, "Tacos", expiresAt ?? Now.AddSeconds(600), Now);

    [Fact]
    public void Pendiente_pagada_queda_Paid_con_la_hora_del_pago()
    {
        var sale = Pending();

        Assert.True(sale.MarkPaid(Now.AddMinutes(2)));

        Assert.Equal(SaleStatus.Paid, sale.Status);
        Assert.Equal(Now.AddMinutes(2), sale.PaidAt);
    }

    [Theory]
    [InlineData(SaleStatus.Expired)]
    [InlineData(SaleStatus.Cancelled)]
    public void Un_pago_tardio_tambien_la_deja_pagada(SaleStatus status)
    {
        var sale = Pending();
        sale.Status = status;

        Assert.True(sale.MarkPaid(Now.AddMinutes(11)));

        Assert.Equal(SaleStatus.Paid, sale.Status);
    }

    [Fact]
    public void Un_pago_adicional_no_cambia_la_hora_del_primero()
    {
        var sale = Pending();
        sale.MarkPaid(Now.AddMinutes(1));

        Assert.False(sale.MarkPaid(Now.AddMinutes(3)));

        Assert.Equal(SaleStatus.Paid, sale.Status);
        Assert.Equal(Now.AddMinutes(1), sale.PaidAt);
    }

    [Fact]
    public void Pendiente_se_puede_cancelar()
    {
        var sale = Pending();

        Assert.True(sale.CanCancel);
        sale.Cancel(Now.AddMinutes(1));

        Assert.Equal(SaleStatus.Cancelled, sale.Status);
        Assert.Equal(Now.AddMinutes(1), sale.CancelledAt);
        Assert.False(sale.CanCancel);
    }

    [Theory]
    [InlineData(SaleStatus.Paid)]
    [InlineData(SaleStatus.Expired)]
    [InlineData(SaleStatus.Cancelled)]
    public void Solo_una_pendiente_se_cancela(SaleStatus status)
    {
        var sale = Pending();
        sale.Status = status;

        Assert.False(sale.CanCancel);
        Assert.Throws<InvalidOperationException>(() => sale.Cancel(Now));
        Assert.Equal(status, sale.Status);
    }

    [Fact]
    public void Expira_cuando_vence_el_QR()
    {
        var sale = Pending();

        Assert.False(sale.ExpireIfDue(Now.AddSeconds(599)));
        Assert.Equal(SaleStatus.Pending, sale.Status);

        Assert.True(sale.ExpireIfDue(Now.AddSeconds(600)));
        Assert.Equal(SaleStatus.Expired, sale.Status);
    }

    [Theory]
    [InlineData(SaleStatus.Paid)]
    [InlineData(SaleStatus.Cancelled)]
    [InlineData(SaleStatus.Expired)]
    public void Solo_expira_una_pendiente(SaleStatus status)
    {
        var sale = Pending();
        sale.Status = status;

        Assert.False(sale.ExpireIfDue(Now.AddHours(1)));
        Assert.Equal(status, sale.Status);
    }

    [Fact]
    public void Sin_expiracion_nunca_expira()
    {
        var sale = Sale.StartDirect(7, 1, 150m, "Tacos", expiresAt: null, Now);

        Assert.False(sale.ExpireIfDue(Now.AddYears(1)));
        Assert.Equal(SaleStatus.Pending, sale.EffectiveStatus(Now.AddYears(1)));
    }

    [Fact]
    public void El_estatus_efectivo_de_una_pendiente_vencida_es_Expired()
    {
        var sale = Pending();

        Assert.Equal(SaleStatus.Pending, sale.EffectiveStatus(Now.AddSeconds(599)));
        Assert.Equal(SaleStatus.Expired, sale.EffectiveStatus(Now.AddSeconds(600)));
        Assert.Equal(SaleStatus.Pending, sale.Status); // no cambia: lo hace el job
    }

    [Fact]
    public void El_estatus_efectivo_de_una_pagada_sigue_siendo_Paid()
    {
        var sale = Pending();
        sale.MarkPaid(Now.AddSeconds(30));

        Assert.Equal(SaleStatus.Paid, sale.EffectiveStatus(Now.AddHours(1)));
    }
}
