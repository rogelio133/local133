using System.Globalization;
using Local133.Domain.Common;
using Local133.Domain.Sales;
using Local133.Web.Models;

namespace Local133.Web.Areas.Merchant.Models;

/// <summary>Rutas del historial de ventas (relativas a la raíz del sitio, para <c>Url.Content("~" + …)</c>).</summary>
public static class SalesUrls
{
    public const string List = "/app/ventas";

    /// <summary>Exportación a Excel (5.9, Pro), con los mismos filtros que la lista.</summary>
    public const string Export = "/app/ventas/exportar";

    /// <summary>Corte de caja diario (5.9, Pro).</summary>
    public const string CashCut = "/app/ventas/corte";

    /// <summary>Corte de caja de un día; hoy va sin <c>?fecha=</c>.</summary>
    public static string CashCutFor(DateOnly day, DateOnly today) =>
        day >= today ? CashCut : $"{CashCut}?fecha={SaleFormats.IsoDate(day)}";

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

    /// <summary>Búsqueda por folio o concepto (5.9); <c>null</c> = sin búsqueda.</summary>
    public string? Query { get; init; }

    public DateOnly Today { get; init; }

    /// <summary>El plan permite exportar a Excel (Pro).</summary>
    public bool CanExport { get; init; }

    /// <summary>El plan trae el corte de caja diario (Básico y Pro; no el vencido).</summary>
    public bool HasCashCut { get; init; }

    /// <summary>El vendedor tiene al menos una venta (sin filtros).</summary>
    public bool HasAnySales { get; init; }

    public bool HasDateFilter => From is not null || To is not null;

    public bool HasSearch => Query is not null;

    public bool IsFiltered => Status != SaleStatusFilter.All || HasDateFilter || HasSearch;

    /// <summary>URL de la lista con el estatus actual y los días de hoy (atajo «Hoy») o de los últimos 7.</summary>
    public string UrlForLastDays(int days) => UrlFor(from: Today.AddDays(1 - days), to: Today);

    /// <summary>
    /// URL de la lista con estos filtros, cambiando estatus, página o días (<paramref name="keepDates"/> = false quita
    /// las fechas; <paramref name="keepQuery"/> = false quita la búsqueda).
    /// </summary>
    public string UrlFor(
        SaleStatusFilter? status = null, int? page = null, bool keepDates = true, DateOnly? from = null, DateOnly? to = null,
        bool keepQuery = true) =>
        UrlWithFilters(SalesUrls.List, status, page, keepDates, from, to, keepQuery);

    /// <summary>Exportación a Excel con los filtros de la lista (sin la página).</summary>
    public string ExportUrl => UrlWithFilters(SalesUrls.Export, null, null, true, null, null, true);

    private string UrlWithFilters(
        string path, SaleStatusFilter? status, int? page, bool keepDates, DateOnly? from, DateOnly? to, bool keepQuery)
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

        if (keepQuery && Query is { } search)
        {
            query.Add($"{Services.SaleSearch.QueryKey}={Uri.EscapeDataString(search)}");
        }

        if (page is > 1)
        {
            query.Add($"{PageInfo.QueryKey}={page.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        return query.Count == 0 ? path : $"{path}?{string.Join('&', query)}";
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

/// <summary>Textos cortos de cómo se cobró, para el corte de caja y la exportación (5.9).</summary>
public static class SaleChannelText
{
    /// <summary>«QR», «Celular» o «QR fijo».</summary>
    public static string Method(ChargeMethod method) => method switch
    {
        ChargeMethod.PushNotification => "Celular",
        ChargeMethod.StaticQr => "QR fijo",
        _ => "QR",
    };

    public static string MethodIcon(ChargeMethod method) => method switch
    {
        ChargeMethod.PushNotification => "bi-phone",
        ChargeMethod.StaticQr => "bi-shop",
        _ => "bi-qr-code",
    };

    /// <summary>«Monto», «Productos» o «Monto libre» (QR fijo: el cliente escribe el monto).</summary>
    public static string Source(SaleSource source) => source switch
    {
        SaleSource.Cart => "Productos",
        SaleSource.OpenAmount => "Monto libre",
        _ => "Monto",
    };

    public static string SourceIcon(SaleSource source) => source switch
    {
        SaleSource.Cart => "bi-basket",
        SaleSource.OpenAmount => "bi-shop",
        _ => "bi-123",
    };
}

/// <summary>Una venta en la exportación a Excel (5.9). Fechas en hora de México.</summary>
public sealed record SalesExportRow(
    int Folio,
    DateTime CreatedAt,
    string? Concept,
    string Method,
    string Source,
    string Status,
    decimal Amount,
    decimal Collected,
    DateTime? PaidAt,
    string? Products);

/// <summary>Renglón del desglose del corte (por método o por origen).</summary>
public sealed record CashCutBreakdownRow(string Label, string Icon, int SalesCount, decimal Amount)
{
    public string AmountText => SaleQrViewModel.FormatAmount(Amount);

    public string SalesText => SalesCount == 1 ? "1 venta" : $"{SalesCount:N0} ventas";
}

/// <summary>Producto vendido en el día: nombre guardado en la venta, piezas y monto.</summary>
public sealed record CashCutProductRow(string Name, int Quantity, decimal Amount)
{
    public string AmountText => SaleQrViewModel.FormatAmount(Amount);
}

/// <summary>Ventas del día que no se pagaron (pendientes o vencidas): cuántas y por cuánto.</summary>
public sealed record CashCutOpenSales(int Count, decimal Amount, string ListUrl)
{
    public string AmountText => SaleQrViewModel.FormatAmount(Amount);
}

/// <summary>
/// Corte de caja de un día en hora de México (/app/ventas/corte, 5.9): lo cobrado ese día (pagos recibidos), su
/// desglose, los productos vendidos y las ventas de ese día que siguen sin pagar.
/// </summary>
public sealed class CashCutViewModel
{
    /// <summary>Productos que van en el mensaje de WhatsApp (los más vendidos).</summary>
    public const int MessageProducts = 5;

    public required DateOnly Day { get; init; }

    public required DateOnly Today { get; init; }

    public string? BusinessName { get; init; }

    public required decimal Collected { get; init; }

    public required int PaidSalesCount { get; init; }

    public required IReadOnlyList<CashCutBreakdownRow> ByMethod { get; init; }

    public required IReadOnlyList<CashCutBreakdownRow> BySource { get; init; }

    public required IReadOnlyList<CashCutProductRow> Products { get; init; }

    public required CashCutOpenSales Pending { get; init; }

    public required CashCutOpenSales Expired { get; init; }

    public bool IsToday => Day == Today;

    public bool HasPayments => PaidSalesCount > 0;

    /// <summary>«Hoy», «Ayer» o «12 de noviembre».</summary>
    public string DayLabel => SaleFormats.DayLabel(Day, Today);

    /// <summary>«23 de noviembre de 2026», siempre con año (va impreso y en el mensaje).</summary>
    public string FullDateText => Day.ToString("d 'de' MMMM 'de' yyyy", CultureInfo.GetCultureInfo("es-MX"));

    public string CollectedText => SaleQrViewModel.FormatAmount(Collected);

    public string PaidSalesText => PaidSalesCount == 1 ? "1 venta pagada" : $"{PaidSalesCount:N0} ventas pagadas";

    /// <summary>Promedio por venta pagada, redondeado al centavo.</summary>
    public decimal AverageTicket =>
        PaidSalesCount == 0 ? 0m : Math.Round(Collected / PaidSalesCount, 2, MidpointRounding.AwayFromZero);

    public string AverageTicketText => SaleQrViewModel.FormatAmount(AverageTicket);

    public int ProductPieces => Products.Sum(p => p.Quantity);

    public string PreviousUrl => SalesUrls.CashCutFor(Day.AddDays(-1), Today);

    /// <summary><c>null</c> en hoy: no hay corte de días que no han llegado.</summary>
    public string? NextUrl => IsToday ? null : SalesUrls.CashCutFor(Day.AddDays(1), Today);

    /// <summary>Resumen en texto para WhatsApp y «Compartir».</summary>
    public string Message
    {
        get
        {
            var lines = new List<string>
            {
                string.IsNullOrWhiteSpace(BusinessName) ? "Corte de caja" : $"Corte de caja · {BusinessName.Trim()}",
                FullDateText,
                string.Empty,
                $"Cobrado: {CollectedText} ({PaidSalesText})",
            };
            lines.AddRange(ByMethod.Where(r => r.SalesCount > 0).Select(r => $"- {r.Label}: {r.AmountText} ({r.SalesCount:N0})"));
            if (HasPayments)
            {
                lines.Add($"Ticket promedio: {AverageTicketText}");
            }

            if (Products.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("Más vendidos:");
                lines.AddRange(Products.Take(MessageProducts).Select(p => $"- {p.Quantity:N0} {p.Name}"));
            }

            if (Pending.Count > 0 || Expired.Count > 0)
            {
                lines.Add(string.Empty);
                if (Pending.Count > 0)
                {
                    lines.Add($"Pendientes: {Pending.Count:N0} ({Pending.AmountText})");
                }

                if (Expired.Count > 0)
                {
                    lines.Add($"Vencidas: {Expired.Count:N0} ({Expired.AmountText})");
                }
            }

            return string.Join('\n', lines);
        }
    }

    public string WhatsAppUrl => ReceiptLinks.WhatsAppUrl(Message);

    public string WhatsAppAppUrl => ReceiptLinks.WhatsAppAppUrl(Message);

    public string WhatsAppWebUrl => ReceiptLinks.WhatsAppWebUrl(Message);
}
