namespace Local133.Domain.Common;

/// <summary>
/// Calendario del negocio: la hora del centro de México (<c>America/Mexico_City</c>). Los días del historial, el
/// resumen del día y la vigencia de la suscripción se cuentan en esta zona, no en UTC.
/// </summary>
public static class MexicoTime
{
    public const string ZoneId = "America/Mexico_City";

    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(ZoneId);

    /// <summary>Hora local de México de un instante.</summary>
    public static DateTimeOffset ToLocal(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Zone);

    /// <summary>Día (en México) de un instante.</summary>
    public static DateOnly DateOf(DateTimeOffset instant) => DateOnly.FromDateTime(ToLocal(instant).DateTime);

    /// <summary>Hoy en México.</summary>
    public static DateOnly Today(DateTimeOffset now) => DateOf(now);

    /// <summary>
    /// Instantes (UTC) de los días <paramref name="from"/> a <paramref name="to"/> en México, ambos incluidos:
    /// <c>StartUtc</c> es la medianoche de <paramref name="from"/> y <c>EndUtc</c> (exclusivo) la del día siguiente a
    /// <paramref name="to"/>.
    /// </summary>
    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) DayRange(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            throw new ArgumentException("El último día no puede ser anterior al primero.", nameof(to));
        }

        return (StartOfDayUtc(from), StartOfDayUtc(to.AddDays(1)));
    }

    /// <summary>Instantes (UTC) de un día en México.</summary>
    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) DayRange(DateOnly day) => DayRange(day, day);

    /// <summary>
    /// Suma meses en el calendario de México y conserva la hora local: del 31 de enero a las 15:00, un mes llega al
    /// último día de febrero a las 15:00 (28 o 29). El resultado va en UTC.
    /// </summary>
    public static DateTimeOffset AddMonths(DateTimeOffset instant, int months) =>
        FromLocal(ToLocal(instant).DateTime.AddMonths(months));

    /// <summary>Suma días en el calendario de México y conserva la hora local. El resultado va en UTC.</summary>
    public static DateTimeOffset AddDays(DateTimeOffset instant, int days) =>
        FromLocal(ToLocal(instant).DateTime.AddDays(days));

    private static DateTimeOffset StartOfDayUtc(DateOnly day) =>
        FromLocal(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified));

    /// <summary>Instante (UTC) de una hora local de México.</summary>
    private static DateTimeOffset FromLocal(DateTime local)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        // Si la hora no existiera (cambio de horario), se toma la primera hora válida.
        while (Zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }

        var offset = Zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
