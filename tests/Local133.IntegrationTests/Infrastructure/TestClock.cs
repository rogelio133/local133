using Local133.Domain.Common;

namespace Local133.IntegrationTests.Infrastructure;

/// <summary>
/// Reloj del sitio en pruebas: la hora real mientras no se congele. Las pruebas de jobs lo congelan en un instante
/// (cerca de la hora real, para no invalidar la cookie de sesión) y se restablece entre pruebas.
/// </summary>
public sealed class TestClock : TimeProvider
{
    private readonly Lock _lock = new();
    private DateTimeOffset? _frozenAt;

    public DateTimeOffset? FrozenAt
    {
        get { lock (_lock) { return _frozenAt; } }
        set { lock (_lock) { _frozenAt = value; } }
    }

    public override DateTimeOffset GetUtcNow() => FrozenAt ?? base.GetUtcNow();

    /// <summary>Congela el reloj a las 9:00 de hoy en México (la hora del job diario) y devuelve ese instante.</summary>
    public DateTimeOffset FreezeAtMexicoNine() => FreezeAtMexicoNine(MexicoTime.Today(DateTimeOffset.UtcNow));

    /// <summary>Congela el reloj a las 9:00 del día dado en México.</summary>
    public DateTimeOffset FreezeAtMexicoNine(DateOnly day)
    {
        var nine = MexicoTime.DayRange(day).StartUtc.AddHours(9);
        FrozenAt = nine;
        return nine;
    }

    public void Reset() => FrozenAt = null;
}
