using System.Globalization;
using Local133.Domain.Merchants;
using Local133.Domain.Pey;
using Local133.Domain.Subscriptions;
using Local133.Web.Areas.Merchant.Models;
using Local133.Web.Models;

namespace Local133.Web.Areas.Admin.Models;

/// <summary>Rutas de Clientes en el admin (relativas a la raíz, para <c>Url.Content("~" + …)</c>).</summary>
public static class CustomerUrls
{
    public const string List = "/admin/clientes";

    public const string Results = "/admin/clientes/resultados";

    public static string Detail(int id) => $"{List}/{id.ToString(CultureInfo.InvariantCulture)}";

    public static string Tab(int id, CustomerTab tab) => tab switch
    {
        CustomerTab.Account => $"{Detail(id)}/cuenta",
        CustomerTab.Sales => $"{Detail(id)}/ventas",
        CustomerTab.Plan => $"{Detail(id)}/plan",
        _ => Detail(id),
    };
}

/// <summary>Pestañas del detalle de un cliente.</summary>
public enum CustomerTab
{
    General,
    Account,
    Sales,
    Plan,
}

/// <summary>Textos e insignias del admin (estatus del vendedor, plan, cuenta CoDi, tipo de titular).</summary>
public static class AdminLabels
{
    public static string Status(MerchantStatus status) => status switch
    {
        MerchantStatus.PendingBankAccount => "Sin cuenta bancaria",
        MerchantStatus.BankAccountInReview => "Cuenta en revisión",
        MerchantStatus.Active => "Activo",
        MerchantStatus.Suspended => "Suspendido",
        MerchantStatus.Disabled => "Dado de baja",
        _ => status.ToString(),
    };

    public static string StatusBadge(MerchantStatus status) => status switch
    {
        MerchantStatus.BankAccountInReview => "text-bg-warning",
        MerchantStatus.Active => "text-bg-success",
        MerchantStatus.Suspended => "text-bg-danger",
        MerchantStatus.Disabled => "text-bg-dark",
        _ => "text-bg-light border",
    };

    public static string Plan(PlanTier tier) => tier switch
    {
        PlanTier.Onboarding => "Sin prueba",
        PlanTier.Trial => "Prueba",
        PlanTier.Basic => "Básico",
        PlanTier.Pro => "Pro",
        PlanTier.Grace => "Gracia",
        PlanTier.Expired => "Vencido",
        _ => tier.ToString(),
    };

    public static string PlanBadge(PlanTier tier) => tier switch
    {
        PlanTier.Trial => "text-bg-info",
        PlanTier.Basic => "text-bg-success",
        PlanTier.Pro => "text-bg-primary",
        PlanTier.Grace => "text-bg-warning",
        PlanTier.Expired => "text-bg-secondary",
        _ => "text-bg-light border",
    };

    public static string Codi(PeyCodiStatus status) => status switch
    {
        PeyCodiStatus.Pending => "Recibida por Pey",
        PeyCodiStatus.Processing => "En trámite con Banxico",
        PeyCodiStatus.Active => "Activa",
        PeyCodiStatus.Error => "Rechazada",
        _ => status.ToString(),
    };

    public static string CodiBadge(PeyCodiStatus status) => status switch
    {
        PeyCodiStatus.Active => "text-bg-success",
        PeyCodiStatus.Error => "text-bg-danger",
        _ => "text-bg-warning",
    };

    public static string Entity(PeyEntity entity) => entity switch
    {
        PeyEntity.Person => "Persona física",
        PeyEntity.Business => "Empresa (persona moral)",
        _ => entity.ToString(),
    };

    /// <summary>«Pro hasta el 12 de diciembre», «Venció el 3 de noviembre»…; <c>null</c> en el onboarding.</summary>
    public static string? PlanDate(PlanStatus status, DateOnly today) => status.Tier switch
    {
        PlanTier.Trial when (status.LastTrialDay ?? status.LastActiveDay) is { } day => $"Prueba hasta el {SaleFormats.Date(day, today)}",
        PlanTier.Basic or PlanTier.Pro when (status.LastPlanDay ?? status.LastActiveDay) is { } day =>
            $"{status.Plan!.Value.Label()} hasta el {SaleFormats.Date(day, today)}",
        PlanTier.Grace when status.LastGraceDay is { } day => $"Gracia hasta el {SaleFormats.Date(day, today)}",
        PlanTier.Expired when status.LastActiveDay is { } day => $"Venció el {SaleFormats.Date(day, today)}",
        _ => null,
    };
}

/// <summary>Filtro de estatus del vendedor en la URL (<c>?estatus=activos</c>).</summary>
public static class MerchantStatusFilters
{
    public const string QueryKey = "estatus";

    public static readonly IReadOnlyList<MerchantStatus> All =
    [
        MerchantStatus.PendingBankAccount, MerchantStatus.BankAccountInReview, MerchantStatus.Active,
        MerchantStatus.Suspended, MerchantStatus.Disabled,
    ];

    public static string Slug(MerchantStatus status) => status switch
    {
        MerchantStatus.PendingBankAccount => "sin-cuenta",
        MerchantStatus.BankAccountInReview => "en-revision",
        MerchantStatus.Active => "activos",
        MerchantStatus.Suspended => "suspendidos",
        MerchantStatus.Disabled => "de-baja",
        _ => status.ToString().ToLowerInvariant(),
    };

    public static MerchantStatus? Parse(string? slug) =>
        All.Cast<MerchantStatus?>().FirstOrDefault(s => string.Equals(Slug(s!.Value), slug?.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>Filtro del plan en la URL (<c>?plan=gracia</c>).</summary>
public static class PlanTierFilters
{
    public const string QueryKey = "plan";

    /// <summary>Hasta la 5.17 (filtro v2) Básico se cuenta dentro de «pro».</summary>
    public static readonly IReadOnlyList<PlanTier> All =
        [PlanTier.Onboarding, PlanTier.Trial, PlanTier.Pro, PlanTier.Grace, PlanTier.Expired];

    public static string Slug(PlanTier tier) => tier switch
    {
        PlanTier.Onboarding => "sin-prueba",
        PlanTier.Trial => "prueba",
        PlanTier.Pro => "pro",
        PlanTier.Grace => "gracia",
        PlanTier.Basic => "basico",
        PlanTier.Expired => "vencido",
        _ => tier.ToString().ToLowerInvariant(),
    };

    public static PlanTier? Parse(string? slug) =>
        All.Cast<PlanTier?>().FirstOrDefault(t => string.Equals(Slug(t!.Value), slug?.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>Filtro de vencimiento del Pro (prueba, pagado o regalado).</summary>
public enum ExpiryFilter
{
    /// <summary>Vence en los próximos 7 días.</summary>
    Within7Days,

    /// <summary>Vence en los próximos 30 días.</summary>
    Within30Days,

    /// <summary>Ya venció (gracia o gratuito).</summary>
    Expired,
}

public static class ExpiryFilters
{
    public const string QueryKey = "vence";

    public static readonly IReadOnlyList<ExpiryFilter> All = [ExpiryFilter.Within7Days, ExpiryFilter.Within30Days, ExpiryFilter.Expired];

    public static string Slug(ExpiryFilter filter) => filter switch
    {
        ExpiryFilter.Within7Days => "7",
        ExpiryFilter.Within30Days => "30",
        _ => "vencido",
    };

    public static string Label(ExpiryFilter filter) => filter switch
    {
        ExpiryFilter.Within7Days => "Próximos 7 días",
        ExpiryFilter.Within30Days => "Próximos 30 días",
        _ => "Ya venció",
    };

    public static ExpiryFilter? Parse(string? slug) =>
        All.Cast<ExpiryFilter?>().FirstOrDefault(f => string.Equals(Slug(f!.Value), slug?.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>Búsqueda y filtros del listado de clientes.</summary>
public sealed record CustomerFilters(
    string? Query = null, MerchantStatus? Status = null, PlanTier? Plan = null, ExpiryFilter? Expiry = null,
    DateOnly? From = null, DateOnly? To = null)
{
    public const int QueryMaxLength = 100;

    public bool IsFiltered => !string.IsNullOrEmpty(Query) || Status is not null || Plan is not null || Expiry is not null
        || From is not null || To is not null;

    /// <summary>Parámetros de la URL con estos filtros y la página.</summary>
    public string QueryString(int? page = null)
    {
        var query = new List<string>();
        if (!string.IsNullOrEmpty(Query))
        {
            query.Add($"q={Uri.EscapeDataString(Query)}");
        }

        if (Status is { } status)
        {
            query.Add($"{MerchantStatusFilters.QueryKey}={MerchantStatusFilters.Slug(status)}");
        }

        if (Plan is { } plan)
        {
            query.Add($"{PlanTierFilters.QueryKey}={PlanTierFilters.Slug(plan)}");
        }

        if (Expiry is { } expiry)
        {
            query.Add($"{ExpiryFilters.QueryKey}={ExpiryFilters.Slug(expiry)}");
        }

        if (From is { } from)
        {
            query.Add($"desde={SaleFormats.IsoDate(from)}");
        }

        if (To is { } to)
        {
            query.Add($"hasta={SaleFormats.IsoDate(to)}");
        }

        if (page is > 1)
        {
            query.Add($"{PageInfo.QueryKey}={page.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        return query.Count == 0 ? string.Empty : "?" + string.Join('&', query);
    }
}

/// <summary>Fila del listado de clientes.</summary>
/// <param name="Name">Nombre de la persona; sin él, el del negocio; sin ninguno, el correo.</param>
/// <param name="BusinessName">Negocio, solo si no es ya <paramref name="Name"/>.</param>
public sealed record CustomerRow(
    int Id, string Name, string? BusinessName, string Email, MerchantStatus Status, PlanStatus Plan, string? PlanDateText,
    PeyCodiStatus? CodiStatus, string CreatedAtText);

/// <summary>Listado de clientes (/admin/clientes).</summary>
public sealed class CustomerListViewModel
{
    public const int PageSize = 20;

    public required IReadOnlyList<CustomerRow> Rows { get; init; }

    public required PageInfo Page { get; init; }

    public required CustomerFilters Filters { get; init; }

    public DateOnly Today { get; init; }

    /// <summary>Hay al menos un vendedor registrado (sin filtros).</summary>
    public bool HasAnyMerchants { get; init; }

    public string UrlFor(int? page = null) => CustomerUrls.List + Filters.QueryString(page);
}

/// <summary>Encabezado y pestañas comunes del detalle.</summary>
public sealed record CustomerHeaderViewModel(
    int Id, string Name, string? BusinessName, string Email, MerchantStatus Status, PlanTier Tier, CustomerTab Tab);

/// <summary>Pestaña General.</summary>
public sealed record CustomerGeneralViewModel(
    CustomerHeaderViewModel Header,
    string? OwnerName,
    string? BusinessName,
    string Email,
    bool EmailConfirmed,
    IReadOnlyList<string> ExternalLogins,
    string CreatedAtText,
    string? LegalAcceptedText,
    string? ActivationEmailSentText);

/// <summary>Pestaña Cuenta (cuenta bancaria). <see cref="MaskedClabe"/> es lo único que se muestra de la CLABE.</summary>
public sealed record CustomerAccountViewModel(CustomerHeaderViewModel Header, CustomerBankAccountView? Account);

public sealed record CustomerBankAccountView(
    string HolderName,
    string MaskedClabe,
    string Alias,
    PeyEntity Entity,
    string PeyBankAccountId,
    PeyCodiStatus CodiStatus,
    string? ErrorMessage,
    string SubmittedAtText,
    string StatusUpdatedAtText,
    string? StatusCheckedAtText);

/// <summary>Fila de la pestaña Ventas.</summary>
public sealed record CustomerSaleRow(
    int Id, int Folio, string CreatedAtText, string? ConceptText, string AmountText, Domain.Sales.SaleStatus Status,
    Domain.Sales.ChargeMethod Method, Domain.Sales.SaleSource Source, string? MaskedPhone);

/// <summary>Pestaña Ventas: resumen y lista paginada con filtro de estatus.</summary>
public sealed class CustomerSalesViewModel
{
    public const int PageSize = 20;

    public required CustomerHeaderViewModel Header { get; init; }

    public required string CollectedTotalText { get; init; }

    public required string CollectedMonthText { get; init; }

    public int PaidSalesCount { get; init; }

    /// <summary>Cobros concretados en el mes (los que cuentan para el límite del gratuito).</summary>
    public int MonthPaidCount { get; init; }

    /// <summary>Cobros generados en el mes.</summary>
    public int MonthGeneratedCount { get; init; }

    /// <summary>Límite de cobros concretados si el plan lo tiene (gratuito); <c>null</c> sin límite.</summary>
    public int? MonthlyLimit { get; init; }

    public required IReadOnlyList<CustomerSaleRow> Rows { get; init; }

    public required PageInfo Page { get; init; }

    public SaleStatusFilter Status { get; init; }

    public string UrlFor(SaleStatusFilter? status = null, int? page = null)
    {
        var query = new List<string>();
        if (SaleStatusFilters.Slug(status ?? Status) is { } slug)
        {
            query.Add($"{SaleStatusFilters.QueryKey}={slug}");
        }

        if (page is > 1)
        {
            query.Add($"{PageInfo.QueryKey}={page.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        var path = CustomerUrls.Tab(Header.Id, CustomerTab.Sales);
        return query.Count == 0 ? path : $"{path}?{string.Join('&', query)}";
    }
}
