using System.Globalization;
using Local133.Domain.Common;
using Local133.Domain.Sales;
using Local133.Web.Models;

namespace Local133.Web.Areas.Merchant.Models;

/// <summary>Rutas del historial de ventas (relativas a la raíz del sitio, para <c>Url.Content("~" + …)</c>).</summary>
public static class SalesUrls
{
    public const string List = "/app/ventas";

    public static string Detail(int saleId) => $"/app/ventas/{saleId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Redirige a WhatsApp con el recibo, al chat del celular al que se cobró.</summary>
    public static string CustomerWhatsApp(int saleId) => $"{Detail(saleId)}/whatsapp";

    /// <summary>Pantalla del cobro (QR o aviso del push) de la venta.</summary>
    public static string Charge(int saleId) => $"/app/cobrar/venta/{saleId.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>Filtro de estatus del historial (sobre el estatus efectivo: una pendiente vencida cuenta como vencida).</summary>
public enum SaleStatusFilter
{
    All,
    Paid,
    Pending,
    Expired,
    Cancelled,
}

public static class SaleStatusFilters
{
    /// <summary>Parámetro de la URL: <c>?estado=pagadas</c>.</summary>
    public const string QueryKey = "estado";

    public static readonly IReadOnlyList<SaleStatusFilter> All =
        [SaleStatusFilter.All, SaleStatusFilter.Paid, SaleStatusFilter.Pending, SaleStatusFilter.Expired, SaleStatusFilter.Cancelled];

    public static string? Slug(SaleStatusFilter filter) => filter switch
    {
        SaleStatusFilter.Paid => "pagadas",
        SaleStatusFilter.Pending => "pendientes",
        SaleStatusFilter.Expired => "vencidas",
        SaleStatusFilter.Cancelled => "canceladas",
        _ => null,
    };

    public static string Label(SaleStatusFilter filter) => filter switch
    {
        SaleStatusFilter.Paid => "Pagadas",
        SaleStatusFilter.Pending => "Pendientes",
        SaleStatusFilter.Expired => "Vencidas",
        SaleStatusFilter.Cancelled => "Canceladas",
        _ => "Todas",
    };

    /// <summary>Valor de la URL ⇒ filtro; desconocido o vacío ⇒ todas.</summary>
    public static SaleStatusFilter Parse(string? slug) =>
        All.FirstOrDefault(f => f != SaleStatusFilter.All && string.Equals(Slug(f), slug?.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>Textos e insignias del estatus de una venta (historial y detalle).</summary>
public static class SaleStatusText
{
    public static string Label(SaleStatus status) => status switch
    {
        SaleStatus.Paid => "Pagada",
        SaleStatus.Pending => "Pendiente",
        SaleStatus.Expired => "Vencida",
        SaleStatus.Cancelled => "Cancelada",
        _ => status.ToString(),
    };

    public static string BadgeClass(SaleStatus status) => status switch
    {
        SaleStatus.Paid => "text-bg-success",
        SaleStatus.Pending => "text-bg-warning",
        SaleStatus.Cancelled => "text-bg-light border",
        _ => "text-bg-secondary",
    };

    /// <summary>Ícono (Bootstrap Icons) de cómo se cobró.</summary>
    public static string MethodIcon(ChargeMethod method, SaleSource source) => (method, source) switch
    {
        (_, SaleSource.Cart) => "bi-basket",
        (ChargeMethod.PushNotification, _) => "bi-phone",
        _ => "bi-qr-code",
    };

    public static string MethodLabel(ChargeMethod method, SaleSource source)
    {
        var how = method switch
        {
            ChargeMethod.PushNotification => "Cobro al celular",
            ChargeMethod.StaticQr => "QR fijo del puesto",
            _ => "QR CoDi",
        };
        return source == SaleSource.Cart ? $"{how} · con productos" : how;
    }
}

/// <summary>Formatos de fechas y montos en hora del centro de México.</summary>
public static class SaleFormats
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-MX");

    /// <summary>«3:45 p. m.», con espacios de no separación (la hora no se parte entre renglones).</summary>
    public static string Time(DateTimeOffset instant) =>
        MexicoTime.ToLocal(instant).ToString("h:mm tt", Spanish).Replace(' ', ' ');

    /// <summary>«Hoy», «Ayer», «12 de noviembre» o, de otro año, «12 de noviembre de 2025».</summary>
    public static string DayLabel(DateOnly day, DateOnly today)
    {
        if (day == today)
        {
            return "Hoy";
        }

        if (day == today.AddDays(-1))
        {
            return "Ayer";
        }

        return Date(day, today);
    }

    /// <summary>«12 de noviembre» o, de otro año, «12 de noviembre de 2025».</summary>
    public static string Date(DateOnly day, DateOnly today) =>
        day.ToString(day.Year == today.Year ? "d 'de' MMMM" : "d 'de' MMMM 'de' yyyy", Spanish);

    /// <summary>«12 de noviembre, 3:45 p. m.» (año incluido si no es el actual).</summary>
    public static string DateTime(DateTimeOffset instant, DateOnly today) =>
        $"{Date(MexicoTime.DateOf(instant), today)}, {Time(instant)}";

    /// <summary>Fecha del filtro en la URL y en <c>input type=date</c>.</summary>
    public static string IsoDate(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>Resumen del día (hora de México): lo cobrado hoy y lo que sigue esperando pago.</summary>
public sealed record DaySummaryViewModel(decimal Collected, int PaidSalesCount, int PendingCount)
{
    public string CollectedText => SaleQrViewModel.FormatAmount(Collected);

    public string PaidSalesText => PaidSalesCount == 1 ? "1 venta pagada" : $"{PaidSalesCount:N0} ventas pagadas";

    public string PendingText => PendingCount == 1 ? "1 esperando pago" : $"{PendingCount:N0} esperando pago";
}

/// <summary>Fila del historial.</summary>
public sealed record SaleHistoryRow(
    int Id, int Folio, DateOnly Day, string TimeText, string? ConceptText, string AmountText, SaleStatus Status,
    ChargeMethod Method, SaleSource Source);

/// <summary>Ventas de un mismo día (en México) dentro de la página.</summary>
public sealed record SaleHistoryDay(string Label, IReadOnlyList<SaleHistoryRow> Rows);

/// <summary>Historial de ventas (/app/ventas): resumen del día, filtros y lista paginada.</summary>
public sealed class SalesHistoryViewModel
{
    public const int PageSize = 20;

    public required DaySummaryViewModel Summary { get; init; }

    public required IReadOnlyList<SaleHistoryDay> Days { get; init; }

    public required PageInfo Page { get; init; }

    public SaleStatusFilter Status { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public DateOnly Today { get; init; }

    /// <summary>El vendedor tiene al menos una venta (sin filtros).</summary>
    public bool HasAnySales { get; init; }

    public bool HasDateFilter => From is not null || To is not null;

    public bool IsFiltered => Status != SaleStatusFilter.All || HasDateFilter;

    /// <summary>URL de la lista con el estatus actual y los días de hoy (atajo «Hoy») o de los últimos 7.</summary>
    public string UrlForLastDays(int days) => UrlFor(from: Today.AddDays(1 - days), to: Today);

    /// <summary>
    /// URL de la lista con estos filtros, cambiando estatus, página o días (<paramref name="keepDates"/> = false quita
    /// las fechas).
    /// </summary>
    public string UrlFor(
        SaleStatusFilter? status = null, int? page = null, bool keepDates = true, DateOnly? from = null, DateOnly? to = null)
    {
        var query = new List<string>();
        if (SaleStatusFilters.Slug(status ?? Status) is { } slug)
        {
            query.Add($"{SaleStatusFilters.QueryKey}={slug}");
        }

        from ??= keepDates ? From : null;
        to ??= keepDates ? To : null;
        if (from is { } fromDay)
        {
            query.Add($"desde={SaleFormats.IsoDate(fromDay)}");
        }

        if (to is { } toDay)
        {
            query.Add($"hasta={SaleFormats.IsoDate(toDay)}");
        }

        if (page is > 1)
        {
            query.Add($"{PageInfo.QueryKey}={page.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        return query.Count == 0 ? SalesUrls.List : $"{SalesUrls.List}?{string.Join('&', query)}";
    }

    /// <summary>«Hoy», «Del 1 de noviembre al 14 de noviembre», «Desde el 1 de noviembre»…</summary>
    public string? DateFilterText => (From, To) switch
    {
        ({ } f, { } t) when f == t => SaleFormats.DayLabel(f, Today),
        ({ } f, { } t) => $"Del {SaleFormats.Date(f, Today)} al {SaleFormats.Date(t, Today)}",
        ({ } f, null) => $"Desde el {SaleFormats.Date(f, Today)}",
        (null, { } t) => $"Hasta el {SaleFormats.Date(t, Today)}",
        _ => null,
    };
}

/// <summary>Pago de una venta tal como lo ve el vendedor.</summary>
public sealed record SalePaymentView(string PaidAtText, string AmountText, string? TrackingKey);

/// <summary>Detalle de una venta (/app/ventas/{id}).</summary>
public sealed record SaleDetailViewModel(
    int Id,
    int Folio,
    string AmountText,
    string? ConceptText,
    SaleStatus Status,
    ChargeMethod Method,
    SaleSource Source,
    string? MaskedPhone,
    string CreatedAtText,
    string? PaidAtText,
    bool IsAwaitingPayment,
    IReadOnlyList<SaleItemView> Items,
    IReadOnlyList<SalePaymentView> Payments,
    ReceiptShare? Receipt)
{
    public bool IsCart => Items.Count > 0;

    public string ItemsSummary
    {
        get
        {
            var count = Items.Sum(i => i.Quantity);
            return count == 1 ? "1 producto" : $"{count:N0} productos";
        }
    }

    public string StatusLabel => SaleStatusText.Label(Status);

    public string StatusBadgeClass => SaleStatusText.BadgeClass(Status);

    public string MethodLabel => SaleStatusText.MethodLabel(Method, Source);

    public string MethodIcon => SaleStatusText.MethodIcon(Method, Source);
}
