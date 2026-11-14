using Local133.Domain.Sales;

namespace Local133.UnitTests.Sales;

/// <summary>Venta por monto directo: monto, concepto, QR, expiración y referencia numérica.</summary>
public class SaleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private const string Concept = "Tacos Don Ramón · 2 tacos";

    [Theory]
    [InlineData("0.01")]
    [InlineData("1")]
    [InlineData("150.5")]
    [InlineData("150.50")]
    [InlineData("999999.99")]
    public void Acepta_montos_de_un_centavo_al_maximo_con_hasta_2_decimales(string text)
    {
        var amount = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        var sale = Sale.StartDirect(7, 1, amount, "Venta 1", Now.AddSeconds(600), Now);

        Assert.True(Sale.IsValidAmount(amount));
        Assert.Equal(amount, sale.Amount);
        Assert.Equal(SaleStatus.Pending, sale.Status);
        Assert.Equal(ChargeMethod.DynamicQr, sale.Method);
        Assert.Equal(SaleSource.DirectAmount, sale.Source);
        Assert.Equal(7, sale.MerchantId);
        Assert.Equal(Now, sale.CreatedAt);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.00")]
    [InlineData("-0.01")]
    [InlineData("-10")]
    [InlineData("1.005")]
    [InlineData("0.001")]
    [InlineData("1000000")]
    public void Rechaza_montos_invalidos(string text)
    {
        var amount = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        Assert.False(Sale.IsValidAmount(amount));
        Assert.Throws<ArgumentOutOfRangeException>(() => Sale.StartDirect(7, 1, amount, Concept, null, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void El_folio_empieza_en_1(int folio) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Sale.StartDirect(7, folio, 10, Concept, null, Now));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(9_999_999, 9_999_999)]
    [InlineData(10_000_000, 0)]
    [InlineData(10_000_123, 123)]
    public void NumRef_cabe_en_7_digitos(int folio, int expected) =>
        Assert.Equal(expected, Sale.StartDirect(7, folio, 10, Concept, null, Now).NumRef);

    [Fact]
    public void El_concepto_se_guarda_sin_espacios_en_los_extremos_y_sin_recortar()
    {
        var concept = new string('a', Sale.ConceptMaxLength);

        Assert.Equal(concept, Sale.StartDirect(7, 1, 10, $"  {concept}  ", null, Now).Concept);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void El_concepto_es_obligatorio(string concept) =>
        Assert.ThrowsAny<ArgumentException>(() => Sale.StartDirect(7, 1, 10, concept, null, Now));

    [Fact]
    public void El_concepto_no_pasa_del_maximo() =>
        Assert.ThrowsAny<ArgumentException>(
            () => Sale.StartDirect(7, 1, 10, new string('a', Sale.ConceptMaxLength + 1), null, Now));

    [Theory]
    [InlineData("Tacos Don Ramón", "2 tacos al pastor", "Tacos Don Ramón · 2 tacos al pastor")]
    [InlineData("  Tacos Don Ramón ", "  2 tacos  ", "Tacos Don Ramón · 2 tacos")]
    [InlineData(null, "2 tacos", "2 tacos")]
    [InlineData("   ", " 2 tacos ", "2 tacos")]
    public void BuildConcept_antepone_el_nombre_del_negocio(string? businessName, string text, string expected) =>
        Assert.Equal(expected, Sale.BuildConcept(businessName, text));

    [Fact]
    public void El_concepto_mas_largo_posible_cabe_en_la_columna()
    {
        var concept = Sale.BuildConcept(
            new string('n', Domain.Merchants.Merchant.BusinessNameMaxLength), new string('c', Sale.ConceptTextMaxLength));

        Assert.True(concept.Length <= Sale.ConceptMaxLength);
    }

    [Theory]
    [InlineData("2 tacos al pastor")]
    [InlineData("Café con pan")]
    [InlineData("Piñata ÁÉÍÓÚ áéíóú Üü Ññ")]
    [InlineData("Pedido #12, mesa 3/4 (para llevar) - Gracias.")]
    [InlineData("  con espacios al inicio y al final  ")]
    public void Acepta_letras_acentos_numeros_y_puntuacion_basica(string text) =>
        Assert.True(Sale.IsValidConceptText(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("🌮")]
    [InlineData("Tacos 🌮")]
    [InlineData("<b>hola</b>")]
    [InlineData("comillas \"dobles\"")]
    [InlineData("correo@prueba.mx")]
    [InlineData("línea\nnueva")]
    public void Rechaza_conceptos_vacios_o_con_caracteres_no_permitidos(string? text) =>
        Assert.False(Sale.IsValidConceptText(text));

    [Fact]
    public void El_concepto_del_vendedor_admite_hasta_100_caracteres()
    {
        Assert.True(Sale.IsValidConceptText(new string('a', Sale.ConceptTextMaxLength)));
        Assert.False(Sale.IsValidConceptText(new string('a', Sale.ConceptTextMaxLength + 1)));
    }

    [Fact]
    public void AttachQr_guarda_el_IDC_y_el_contenido_una_sola_vez()
    {
        var sale = Sale.StartDirect(7, 1, 10, Concept, null, Now);

        sale.AttachQr("IDC1", "{\"TYP\":20}");

        Assert.Equal("IDC1", sale.PeyQrId);
        Assert.Equal("{\"TYP\":20}", sale.QrString);
        Assert.Throws<InvalidOperationException>(() => sale.AttachQr("IDC2", "x"));
    }

    [Theory]
    [InlineData("", "x")]
    [InlineData("IDC1", " ")]
    public void AttachQr_exige_IDC_y_contenido(string idc, string qrString) =>
        Assert.ThrowsAny<ArgumentException>(() => Sale.StartDirect(7, 1, 10, Concept, null, Now).AttachQr(idc, qrString));

    [Fact]
    public void Cuenta_los_segundos_restantes_y_vence_al_llegar_a_cero()
    {
        var sale = Sale.StartDirect(7, 1, 10, Concept, Now.AddSeconds(600), Now);

        Assert.Equal(600, sale.RemainingSeconds(Now));
        Assert.Equal(599, sale.RemainingSeconds(Now.AddMilliseconds(500)));
        Assert.Equal(1, sale.RemainingSeconds(Now.AddSeconds(599)));
        Assert.False(sale.IsExpiredAt(Now.AddSeconds(599)));

        Assert.Equal(0, sale.RemainingSeconds(Now.AddSeconds(600)));
        Assert.True(sale.IsExpiredAt(Now.AddSeconds(600)));
        Assert.Equal(0, sale.RemainingSeconds(Now.AddHours(1)));
        Assert.Equal(SaleStatus.Pending, sale.Status); // el estatus lo cambia el job de expiración
    }

    [Fact]
    public void Sin_expiracion_nunca_vence()
    {
        var sale = Sale.StartDirect(7, 1, 10, Concept, expiresAt: null, Now);

        Assert.Null(sale.RemainingSeconds(Now.AddYears(1)));
        Assert.False(sale.IsExpiredAt(Now.AddYears(1)));
    }

    // --- Cobro por celular (notificación push) ----------------------------------------------------------------------

    private const string Phone = "5512345678";

    [Fact]
    public void StartPush_crea_una_venta_por_monto_cobrada_al_celular()
    {
        var sale = Sale.StartPush(7, 3, 150.50m, $"  {Concept}  ", Phone, Now.AddSeconds(600), Now);

        Assert.Equal(ChargeMethod.PushNotification, sale.Method);
        Assert.Equal(SaleSource.DirectAmount, sale.Source);
        Assert.Equal(SaleStatus.Pending, sale.Status);
        Assert.Equal(150.50m, sale.Amount);
        Assert.Equal(Concept, sale.Concept);
        Assert.Equal(Phone, sale.PhoneNumber);
        Assert.Equal(3, sale.Folio);
        Assert.Equal(Now.AddSeconds(600), sale.ExpiresAt);
        Assert.Null(sale.PeyTransactionId);
        Assert.Null(sale.PeyQrId);
        Assert.False(sale.HasPaymentRequest);
    }

    [Theory]
    [InlineData("")]
    [InlineData("551234567")]
    [InlineData("55 1234 5678")] // debe llegar ya normalizado
    [InlineData("+525512345678")]
    public void StartPush_exige_un_celular_de_10_digitos(string phone) =>
        Assert.Throws<ArgumentException>(() => Sale.StartPush(7, 1, 10, Concept, phone, Now.AddSeconds(600), Now));

    [Theory]
    [InlineData("0")]
    [InlineData("1.001")]
    [InlineData("1000000")]
    public void StartPush_valida_el_monto_igual_que_el_QR(string text)
    {
        var amount = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Throws<ArgumentOutOfRangeException>(() => Sale.StartPush(7, 1, amount, Concept, Phone, Now.AddSeconds(600), Now));
    }

    [Fact]
    public void StartPush_valida_concepto_y_folio()
    {
        Assert.ThrowsAny<ArgumentException>(() => Sale.StartPush(7, 1, 10, " ", Phone, Now.AddSeconds(600), Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => Sale.StartPush(7, 0, 10, Concept, Phone, Now.AddSeconds(600), Now));
    }

    [Fact]
    public void AttachPushNotification_guarda_la_transaccion_una_sola_vez()
    {
        var sale = Sale.StartPush(7, 1, 10, Concept, Phone, Now.AddSeconds(600), Now);

        sale.AttachPushNotification(" 01J5KXYZ1234567890ABCD ", "12345678901234567890");

        Assert.Equal("01J5KXYZ1234567890ABCD", sale.PeyTransactionId);
        Assert.Equal("12345678901234567890", sale.PeyCodiId);
        Assert.True(sale.HasPaymentRequest);
        Assert.Throws<InvalidOperationException>(() => sale.AttachPushNotification("OTRA", null));
    }

    [Fact]
    public void AttachPushNotification_solo_aplica_al_cobro_por_celular()
    {
        var sale = Sale.StartDirect(7, 1, 10, Concept, Now.AddSeconds(600), Now);

        Assert.Throws<InvalidOperationException>(() => sale.AttachPushNotification("01J5KXYZ1234567890ABCD", null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void AttachPushNotification_exige_la_transaccion(string transactionId)
    {
        var sale = Sale.StartPush(7, 1, 10, Concept, Phone, Now.AddSeconds(600), Now);

        Assert.Throws<ArgumentException>(() => sale.AttachPushNotification(transactionId, null));
    }

    [Fact]
    public void Un_cobro_por_celular_vence_y_se_paga_como_el_QR()
    {
        var sale = Sale.StartPush(7, 1, 10, Concept, Phone, Now.AddSeconds(600), Now);
        sale.AttachPushNotification("01J5KXYZ1234567890ABCD", null);

        Assert.Equal(SaleStatus.Pending, sale.EffectiveStatus(Now.AddSeconds(599)));
        Assert.Equal(SaleStatus.Expired, sale.EffectiveStatus(Now.AddSeconds(600)));
        Assert.True(sale.ExpireIfDue(Now.AddSeconds(600)));
        Assert.True(sale.MarkPaid(Now.AddSeconds(700))); // el pago manda, también vencida
        Assert.Equal(SaleStatus.Paid, sale.Status);
    }

    [Fact]
    public void HasPaymentRequest_en_QR_depende_del_QR()
    {
        var sale = Sale.StartDirect(7, 1, 10, Concept, Now.AddSeconds(600), Now);
        Assert.False(sale.HasPaymentRequest);

        sale.AttachQr("IDC1", "{\"TYP\":20}");
        Assert.True(sale.HasPaymentRequest);
    }
}
