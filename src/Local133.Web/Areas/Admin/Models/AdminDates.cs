using System.Globalization;

namespace Local133.Web.Areas.Admin.Models;

/// <summary>Fechas de los filtros del admin.</summary>
public static class AdminDates
{
    /// <summary>Fecha <c>AAAA-MM-DD</c> (la que manda <c>input type=date</c>); cualquier otra cosa se ignora.</summary>
    public static DateOnly? Parse(string? text) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    /// <summary>Desde/hasta en orden (si vienen al revés se intercambian).</summary>
    public static (DateOnly? From, DateOnly? To) Ordered(DateOnly? from, DateOnly? to) =>
        from is { } f && to is { } t && t < f ? (t, f) : (from, to);

    /// <summary>Texto de búsqueda sin espacios en los extremos y recortado; vacío ⇒ <c>null</c>.</summary>
    public static string? Query(string? text, int maxLength)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd();
    }
}

/// <summary>Arma <c>?a=b&amp;c=d</c> con los parámetros que tienen valor (escapados).</summary>
public static class AdminQueryString
{
    public static string Build(params (string Key, string? Value)[] parameters)
    {
        var parts = parameters
            .Where(p => !string.IsNullOrEmpty(p.Value))
            .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}")
            .ToList();
        return parts.Count == 0 ? string.Empty : "?" + string.Join('&', parts);
    }

    public static string? Page(int? page) => page is > 1 ? page.Value.ToString(CultureInfo.InvariantCulture) : null;

    public static string? Date(DateOnly? day) => day is { } d ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
}
