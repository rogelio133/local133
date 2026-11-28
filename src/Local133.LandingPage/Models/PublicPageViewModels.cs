using Local133.Web.PublicPages;

namespace Local133.LandingPage.Models;

/// <summary>Recibo público (<c>/recibo/{token}</c>, ajustes 2026-11-27), con los datos del portal.</summary>
public sealed record ReceiptPageViewModel(PublicReceipt Receipt)
{
    /// <summary>«Cobrado con Local133» (Básico y vencido): la portada con el código de invitación del vendedor.</summary>
    public string? PoweredByUrl => Receipt.ReferralCode is { Length: > 0 } code ? InviteUrl(code) : null;

    /// <summary>Portada con el código de invitación (<c>/?codigo=ABC234</c>; ver <c>HomeController.Index</c>).</summary>
    public static string InviteUrl(string referralCode) => "/?codigo=" + Uri.EscapeDataString(referralCode);
}

/// <summary>Catálogo público (<c>/tienda/{slug}</c>, ajustes 2026-11-27), con los datos del portal.</summary>
/// <param name="ListPath">Página del catálogo en la portada (búsqueda y paginación).</param>
public sealed record StorefrontPageViewModel(PublicStorefront Storefront, string ListPath)
{
    /// <summary>Renglones de la dirección (se guardan con <c>\n</c>).</summary>
    public IReadOnlyList<string> AddressLines =>
        Storefront.Address is null ? [] : Storefront.Address.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>«Catálogo hecho con Local133»: la portada con el código de invitación del vendedor.</summary>
    public string MadeWithUrl =>
        Storefront.ReferralCode is { Length: > 0 } code ? ReceiptPageViewModel.InviteUrl(code) : "/";

    public bool HasPrevious => Storefront.Page > 1;

    public bool HasNext => Storefront.Page < Storefront.TotalPages;

    /// <summary>La página <paramref name="page"/> con la misma búsqueda.</summary>
    public string UrlFor(int page)
    {
        var parameters = new List<string>();
        if (Storefront.Query is not null)
        {
            parameters.Add("q=" + Uri.EscapeDataString(Storefront.Query));
        }

        if (page > 1)
        {
            parameters.Add($"{PublicPagesApi.PageQueryKey}={page}");
        }

        return parameters.Count == 0 ? ListPath : $"{ListPath}?{string.Join('&', parameters)}";
    }
}

/// <summary>
/// Página de respaldo del recibo o del catálogo: no existe, no se puede ver o el portal no respondió.
/// </summary>
/// <param name="Kind">Qué no se pudo mostrar (cambia el ícono y el texto).</param>
public sealed record PublicPageMessageViewModel(PublicPageMessageKind Kind, string? BusinessName = null);

public enum PublicPageMessageKind
{
    ReceiptNotFound,
    ReceiptError,
    StorefrontUnavailable,
    StorefrontError,
}
