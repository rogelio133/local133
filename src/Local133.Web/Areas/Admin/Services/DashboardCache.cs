using Local133.Web.Areas.Admin.Models;

namespace Local133.Web.Areas.Admin.Services;

/// <summary>
/// Guarda el tablero del admin <see cref="Duration"/> para no repetir sus consultas cada vez que se abre: la base es la
/// misma que usan los vendedores al cobrar. Una sola petición lo calcula a la vez (las demás esperan y usan el
/// resultado). «Actualizar» lo vuelve a calcular. Singleton; es por proceso (con varias instancias, cada una tiene el
/// suyo).
/// </summary>
public sealed class DashboardCache(TimeProvider clock)
{
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DashboardViewModel? _model;

    public async Task<DashboardViewModel> GetAsync(
        Func<DateTimeOffset, CancellationToken, Task<DashboardViewModel>> build, bool refresh, CancellationToken cancellationToken)
    {
        if (!refresh && Fresh(_model) is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Otra petición pudo calcularlo mientras esperábamos.
            if (!refresh && Fresh(_model) is { } computed)
            {
                return computed;
            }

            var model = await build(clock.GetUtcNow(), cancellationToken);
            _model = model;
            return model;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Lo olvida (pruebas).</summary>
    public void Clear() => _model = null;

    private DashboardViewModel? Fresh(DashboardViewModel? model)
    {
        if (model is null)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        return now >= model.GeneratedAt && now < model.GeneratedAt + Duration ? model : null;
    }
}
