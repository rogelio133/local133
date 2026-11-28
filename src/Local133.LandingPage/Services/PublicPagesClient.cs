using System.Net;
using System.Net.Http.Json;
using Local133.Web.PublicPages;

namespace Local133.LandingPage.Services;

/// <summary>
/// Cliente de la API pública de solo lectura del portal (<c>/api/public</c>, ajustes 2026-11-27): la portada no tiene
/// base, así que el recibo y el catálogo públicos piden sus datos ahí. Dirección base: <c>Landing:AppUrl</c>. Reintenta
/// los GET con el manejador estándar de resiliencia.
/// </summary>
public sealed class PublicPagesClient(HttpClient http)
{
    /// <summary>Recibo de una venta pagada, o <c>null</c> si no existe (404). Otra falla lanza <see cref="HttpRequestException"/>.</summary>
    public Task<PublicReceipt?> GetReceiptAsync(string token, CancellationToken cancellationToken) =>
        GetAsync<PublicReceipt>(PublicPagesApi.Receipt(token), cancellationToken);

    /// <summary>
    /// Catálogo de un vendedor (también el que no se puede ver, con <see cref="PublicStorefront.Available"/> en falso), o
    /// <c>null</c> si la liga no existe (404).
    /// </summary>
    public Task<PublicStorefront?> GetStorefrontAsync(string slug, string? query, int? page, CancellationToken cancellationToken) =>
        GetAsync<PublicStorefront>(PublicPagesApi.Storefront(slug, query, page), cancellationToken);

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        using var response = await http.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new HttpRequestException($"Respuesta vacía de {path}.");
    }
}
