using Local133.Domain.Payments;

namespace Local133.UnitTests.Payments;

/// <summary>Registro de un pago CoDi de una venta.</summary>
public class PaymentTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void El_pago_guarda_los_datos_de_la_transaccion()
    {
        var payment = Payment.Record(7, 42, " 01M3QR8H0RH0XBCKYYD242YXNV ", 150m, Now.AddMinutes(-1), " 0192191042 ", 40012, Now);

        Assert.Equal(7, payment.MerchantId);
        Assert.Equal(42, payment.SaleId);
        Assert.Equal("01M3QR8H0RH0XBCKYYD242YXNV", payment.PeyTransactionId);
        Assert.Equal(150m, payment.Amount);
        Assert.Equal(Now.AddMinutes(-1), payment.PaidAt);
        Assert.Equal("0192191042", payment.TrackingKey);
        Assert.Equal(40012, payment.PayerBankCode);
        Assert.Equal(Now, payment.CreatedAt);
    }

    [Fact]
    public void Un_centavo_se_registra() =>
        Assert.Equal(0.01m, Payment.Record(7, 42, "TX1", 0.01m, Now, null, null, Now).Amount);

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void El_pago_exige_el_id_de_la_transaccion(string transactionId) =>
        Assert.Throws<ArgumentException>(() => Payment.Record(7, 42, transactionId, 10m, Now, null, null, Now));

    [Fact]
    public void El_id_de_la_transaccion_admite_hasta_64_caracteres()
    {
        Assert.Equal(64, Payment.Record(7, 42, new string('A', 64), 10m, Now, null, null, Now).PeyTransactionId.Length);
        Assert.Throws<ArgumentException>(() => Payment.Record(7, 42, new string('A', 65), 10m, Now, null, null, Now));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void El_pago_exige_monto_positivo(string amount) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Payment.Record(7, 42, "TX1", D(amount), Now, null, null, Now));

    [Fact]
    public void Clave_de_rastreo_vacia_queda_nula() =>
        Assert.Null(Payment.Record(7, 42, "TX1", 10m, Now, "  ", null, Now).TrackingKey);

    [Fact]
    public void Clave_de_rastreo_larga_se_recorta_a_64() =>
        Assert.Equal(new string('7', 64), Payment.Record(7, 42, "TX1", 10m, Now, new string('7', 80), null, Now).TrackingKey);

    private static decimal D(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
