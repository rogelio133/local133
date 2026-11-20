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
    public void AddMonths_conserva_la_hora_de_Mexico_y_ajusta_al_ultimo_dia_del_mes()
    {
        // 31 de enero, 12:00 en México (18:00 UTC).
        var start = new DateTimeOffset(2026, 1, 31, 18, 0, 0, TimeSpan.Zero);

        var end = MexicoTime.AddMonths(start, 1);

        Assert.Equal(new DateTimeOffset(2026, 2, 28, 18, 0, 0, TimeSpan.Zero), end);
        Assert.Equal(TimeSpan.Zero, end.Offset);
        Assert.Equal(new DateTimeOffset(2028, 2, 29, 18, 0, 0, TimeSpan.Zero),
            MexicoTime.AddMonths(new DateTimeOffset(2028, 1, 31, 18, 0, 0, TimeSpan.Zero), 1));
    }

    [Fact]
    public void AddMonths_usa_el_dia_de_Mexico_y_no_el_de_UTC()
    {
        // 31 de enero a las 23:00 en México = 1 de febrero a las 05:00 UTC.
        var start = new DateTimeOffset(2026, 2, 1, 5, 0, 0, TimeSpan.Zero);

        Assert.Equal(new DateTimeOffset(2026, 3, 1, 5, 0, 0, TimeSpan.Zero), MexicoTime.AddMonths(start, 1)); // 28/02 23:00 en México
    }

    [Fact]
    public void AddDays_cruza_el_cambio_de_anio_en_hora_de_Mexico()
    {
        var start = new DateTimeOffset(2026, 12, 20, 21, 0, 0, TimeSpan.Zero); // 20/12 15:00 en México

        Assert.Equal(new DateTimeOffset(2027, 1, 19, 21, 0, 0, TimeSpan.Zero), MexicoTime.AddDays(start, 30));
    }

    [Fact]
    public void DayRange_rechaza_un_rango_invertido() =>
        Assert.Throws<ArgumentException>(() => MexicoTime.DayRange(new DateOnly(2026, 11, 14), new DateOnly(2026, 11, 13)));
}
