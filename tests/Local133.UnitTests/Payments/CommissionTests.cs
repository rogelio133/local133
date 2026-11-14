using Local133.Domain.Payments;

namespace Local133.UnitTests.Payments;

/// <summary>Comisión: jerarquía de tasas y redondeo a centavos; la tasa queda guardada en cada pago.</summary>
public class CommissionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 18, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("150", "0.03", "4.50")]
    [InlineData("0.01", "0.03", "0.00")]      // un centavo no genera comisión
    [InlineData("33.33", "0.03", "1.00")]     // 0.9999
    [InlineData("16.50", "0.03", "0.50")]     // 0.495 ⇒ punto medio hacia arriba
    [InlineData("0.50", "0.03", "0.02")]      // 0.015 ⇒ punto medio hacia arriba
    [InlineData("0.49", "0.03", "0.01")]      // 0.0147
    [InlineData("999999.99", "0.03", "30000.00")]
    [InlineData("100", "0.025", "2.50")]
    [InlineData("100", "0", "0.00")]
    public void Calcula_la_comision_redondeada_a_centavos(string amount, string rate, string expected) =>
        Assert.Equal(D(expected), Commission.Calculate(D(amount), D(rate)));

    [Fact]
    public void Sin_tasa_propia_aplica_la_global() =>
        Assert.Equal(0.03m, Commission.EffectiveRate(null, 0.03m));

    [Theory]
    [InlineData("0.02")]
    [InlineData("0")]
    [InlineData("0.05")]
    public void La_tasa_propia_del_vendedor_gana_sobre_la_global(string merchantRate) =>
        Assert.Equal(D(merchantRate), Commission.EffectiveRate(D(merchantRate), 0.03m));

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1")]
    [InlineData("1.5")]
    public void Rechaza_tasas_fuera_de_rango(string rate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Commission.Calculate(10, D(rate)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Commission.EffectiveRate(D(rate), 0.03m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Commission.EffectiveRate(null, D(rate)));
    }

    [Fact]
    public void Rechaza_montos_negativos() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Commission.Calculate(-1, 0.03m));

    [Fact]
    public void El_pago_guarda_la_tasa_y_la_comision_del_momento()
    {
        var payment = Payment.Record(7, 42, " 01M3QR8H0RH0XBCKYYD242YXNV ", 150m, Now.AddMinutes(-1), " 0192191042 ", 40012, 0.03m, Now);

        Assert.Equal(7, payment.MerchantId);
        Assert.Equal(42, payment.SaleId);
        Assert.Equal("01M3QR8H0RH0XBCKYYD242YXNV", payment.PeyTransactionId);
        Assert.Equal(150m, payment.Amount);
        Assert.Equal(Now.AddMinutes(-1), payment.PaidAt);
        Assert.Equal("0192191042", payment.TrackingKey);
        Assert.Equal(40012, payment.PayerBankCode);
        Assert.Equal(0.03m, payment.CommissionRate);
        Assert.Equal(4.50m, payment.CommissionAmount);
        Assert.Null(payment.StatementId);
        Assert.Equal(Now, payment.CreatedAt);
    }

    [Fact]
    public void Cambiar_la_tasa_despues_no_altera_el_pago()
    {
        var payment = Payment.Record(7, 42, "TX1", 100m, Now, null, null, 0.03m, Now);
        var rateLater = Commission.EffectiveRate(0.05m, 0.04m);

        Assert.Equal(0.05m, rateLater);
        Assert.Equal(0.03m, payment.CommissionRate);
        Assert.Equal(3.00m, payment.CommissionAmount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void El_pago_exige_el_id_de_la_transaccion(string transactionId) =>
        Assert.Throws<ArgumentException>(() => Payment.Record(7, 42, transactionId, 10m, Now, null, null, 0.03m, Now));

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void El_pago_exige_monto_positivo(string amount) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Payment.Record(7, 42, "TX1", D(amount), Now, null, null, 0.03m, Now));

    [Fact]
    public void Clave_de_rastreo_vacia_queda_nula() =>
        Assert.Null(Payment.Record(7, 42, "TX1", 10m, Now, "  ", null, 0.03m, Now).TrackingKey);

    private static decimal D(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
