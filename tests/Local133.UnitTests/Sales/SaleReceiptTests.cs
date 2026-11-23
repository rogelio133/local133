using Local133.Domain.Sales;

namespace Local133.UnitTests.Sales;

/// <summary>Paso 2.8: token del recibo público y concepto sin el nombre del negocio.</summary>
public class SaleReceiptTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 18, 0, 0, TimeSpan.Zero);

    private static Sale NewSale() => Sale.StartDirect(1, 1, 150m, "Tacos Don Ramón · 2 tacos", Now.AddMinutes(10), Now);

    [Fact]
    public void Cada_venta_nueva_trae_un_token_de_recibo_distinto()
    {
        var first = NewSale();
        var second = NewSale();

        Assert.True(Sale.IsValidReceiptToken(first.ReceiptToken));
        Assert.Matches("^[0-9a-f]{32}$", first.ReceiptToken);
        Assert.NotEqual(first.ReceiptToken, second.ReceiptToken);
    }

    [Fact]
    public void El_cobro_por_celular_tambien_trae_token()
    {
        var sale = Sale.StartPush(1, 1, 150m, "2 tacos", "5512345678", Now.AddMinutes(10), Now);
        Assert.True(Sale.IsValidReceiptToken(sale.ReceiptToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0123456789abcdef0123456789abcde")]   // 31
    [InlineData("0123456789abcdef0123456789abcdef0")] // 33
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]  // mayúsculas
    [InlineData("0123456789abcdef0123456789abcdeg")]  // no hexadecimal
    [InlineData("../../../etc/passwd/aaaaaaaaaaaaa")]
    public void IsValidReceiptToken_rechaza_lo_que_no_es_un_token(string? token) =>
        Assert.False(Sale.IsValidReceiptToken(token));

    [Fact]
    public void Solo_una_venta_pagada_tiene_recibo()
    {
        var sale = NewSale();
        Assert.False(sale.HasReceipt);

        sale.ExpireIfDue(Now.AddHours(1));
        Assert.False(sale.HasReceipt);

        sale.MarkPaid(Now.AddHours(2));
        Assert.True(sale.HasReceipt);

        var cancelled = NewSale();
        cancelled.Cancel(Now);
        Assert.False(cancelled.HasReceipt);
    }

    [Theory]
    [InlineData("Tacos Don Ramón · 2 tacos al pastor", "2 tacos al pastor")]
    [InlineData("2 tacos al pastor", "2 tacos al pastor")]
    [InlineData("Taquería Vieja · 2 tacos", "2 tacos")] // 4.1: el negocio cambió de nombre en «Mi información»
    [InlineData("Tacos · Don Ramón · 2 tacos", "2 tacos")] // un nombre viejo con el separador (antes no se validaba)
    [InlineData("Tacos Don Ramón · ", "Tacos Don Ramón · ")]
    [InlineData(" · 2 tacos", " · 2 tacos")]
    [InlineData(null, null)]
    public void ConceptTextOf_quita_el_nombre_del_negocio(string? concept, string? expected) =>
        Assert.Equal(expected, Sale.ConceptTextOf(concept));

    [Fact]
    public void ConceptTextOf_es_el_inverso_de_BuildConcept()
    {
        Assert.Equal("Refresco", Sale.ConceptTextOf(Sale.BuildConcept("Tacos Don Ramón", "Refresco")));
        Assert.Equal("Refresco", Sale.ConceptTextOf(Sale.BuildConcept(null, "Refresco")));
    }
}
