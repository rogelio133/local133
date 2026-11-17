using Local133.Domain.Common;

namespace Local133.UnitTests.Common;

/// <summary>Días del negocio en hora del centro de México (UTC−6, sin horario de verano desde 2022).</summary>
public class MexicoTimeTests
{
    [Fact]
    public void DateOf_usa_la_hora_de_Mexico_y_no_UTC()
    {
        // 15 de noviembre 05:59 UTC = 14 de noviembre 23:59 en México.
        Assert.Equal(new DateOnly(2026, 11, 14), MexicoTime.DateOf(new DateTimeOffset(2026, 11, 15, 5, 59, 0, TimeSpan.Zero)));
        // 06:00 UTC = medianoche del 15 en México.
        Assert.Equal(new DateOnly(2026, 11, 15), MexicoTime.DateOf(new DateTimeOffset(2026, 11, 15, 6, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Today_es_el_dia_en_Mexico()
    {
        var now = new DateTimeOffset(2027, 1, 1, 3, 0, 0, TimeSpan.Zero); // 31 de diciembre, 9 p. m. en México
        Assert.Equal(new DateOnly(2026, 12, 31), MexicoTime.Today(now));
    }

    [Fact]
    public void DayRange_de_un_dia_va_de_medianoche_a_medianoche_en_Mexico()
    {
        var (start, end) = MexicoTime.DayRange(new DateOnly(2026, 11, 14));

        Assert.Equal(new DateTimeOffset(2026, 11, 14, 6, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 6, 0, 0, TimeSpan.Zero), end);
        Assert.Equal(TimeSpan.Zero, start.Offset);
    }

    [Fact]
    public void DayRange_incluye_ambos_dias_y_cruza_el_cambio_de_anio()
    {
        var (start, end) = MexicoTime.DayRange(new DateOnly(2026, 12, 30), new DateOnly(2027, 1, 2));

        Assert.Equal(new DateTimeOffset(2026, 12, 30, 6, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2027, 1, 3, 6, 0, 0, TimeSpan.Zero), end);
    }

    [Theory]
    [InlineData(2026, 11, 14, 5, 59, false)] // 13 de noviembre, 11:59 p. m.
    [InlineData(2026, 11, 14, 6, 0, true)]   // 14 de noviembre, 00:00
    [InlineData(2026, 11, 15, 5, 59, true)]  // 14 de noviembre, 11:59 p. m.
    [InlineData(2026, 11, 15, 6, 0, false)]  // 15 de noviembre, 00:00
    public void Los_bordes_del_dia_son_inicio_incluido_y_fin_excluido(int y, int m, int d, int h, int min, bool inside)
    {
        var (start, end) = MexicoTime.DayRange(new DateOnly(2026, 11, 14));
        var instant = new DateTimeOffset(y, m, d, h, min, 0, TimeSpan.Zero);

        Assert.Equal(inside, instant >= start && instant < end);
    }

    [Fact]
    public void DayRange_rechaza_un_rango_invertido() =>
        Assert.Throws<ArgumentException>(() => MexicoTime.DayRange(new DateOnly(2026, 11, 14), new DateOnly(2026, 11, 13)));
}
