namespace Local133.Web.Models;

/// <summary>
/// Página de un listado (base 1). <see cref="For"/> ajusta la página pedida: menor que 1 ⇒ 1, mayor que la última
/// ⇒ la última. La dibuja el parcial <c>_Pager</c>.
/// </summary>
public sealed record PageInfo(int Page, int PageSize, int TotalCount)
{
    /// <summary>Parámetro de la página en la URL (<c>?pagina=2</c>).</summary>
    public const string QueryKey = "pagina";

    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < TotalPages;

    /// <summary>Filas que se saltan para llegar a esta página.</summary>
    public int Skip => (Page - 1) * PageSize;

    public static PageInfo For(int? requestedPage, int pageSize, int totalCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)pageSize);
        var page = Math.Clamp(requestedPage ?? 1, 1, totalPages);
        return new PageInfo(page, pageSize, totalCount);
    }
}

/// <summary>Modelo del parcial <c>_Pager</c>: la página y cómo armar la URL de otra página.</summary>
public sealed record PagerViewModel(PageInfo Page, Func<int, string> UrlForPage, string Label = "Paginación");
