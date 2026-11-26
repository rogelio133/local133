using Local133.LandingPage.Controllers;

namespace Local133.LandingPage.Models;

/// <summary>Encabezado y pie de la portada (<c>Views/Shared/_Header</c> y <c>_Footer</c>), también en las páginas legales.</summary>
/// <param name="SignUpUrl">Destino de «Prueba 30 días gratis»: el portal, o su liga <c>/r/{código}</c> en la portada.</param>
/// <param name="LoginUrl">«Iniciar sesión» en el portal; nunca lleva el código.</param>
/// <param name="HomeUrl">
/// Prefijo de las anclas de las secciones: <c>""</c> en la portada (<c>#precios</c>) y <c>"/"</c> en las demás páginas
/// (<c>/#precios</c>).
/// </param>
public sealed record SiteChromeViewModel(string SignUpUrl, string LoginUrl, string HomeUrl)
{
    /// <summary>Liga a una sección de la portada (<paramref name="id"/> sin <c>#</c>).</summary>
    public string Section(string id) => $"{HomeUrl}#{id}";

    /// <summary>Logo: el inicio de la portada, o la portada desde otra página.</summary>
    public string BrandUrl => HomeUrl.Length == 0 ? "#inicio" : HomeUrl;

    public static SiteChromeViewModel For(string appUrl, string signUpUrl, string homeUrl) =>
        new(signUpUrl, HomeController.PortalUrl(appUrl, HomeController.LoginPath), homeUrl);
}
