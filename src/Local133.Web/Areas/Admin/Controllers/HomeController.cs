using Local133.Application.Auth;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Admin.Models;
using Local133.Web.Areas.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Local133.Web.Areas.Admin.Controllers;

/// <summary>Portal administrador (/admin): solo rol Admin. La entrada lleva a Clientes; el tablero está en /admin/tablero (6.4).</summary>
[Area("Admin")]
[Authorize(Roles = Roles.Admin)]
[Route("admin")]
[ApiExplorerSettings(IgnoreApi = true)]
public class HomeController(AppDbContext db, DashboardCache cache) : Controller
{
    public const string DashboardUrl = "/admin/tablero";

    /// <summary>Parámetro de «Actualizar»: vuelve a calcular el tablero sin esperar a que caduque.</summary>
    public const string RefreshKey = "actualizar";

    [HttpGet("")]
    public IActionResult Index() => Redirect(Url.Content("~" + CustomerUrls.List));

    [HttpGet("tablero")]
    public async Task<IActionResult> Dashboard(
        [FromQuery(Name = RefreshKey)] bool refresh, CancellationToken cancellationToken)
    {
        var model = await cache.GetAsync(
            (now, ct) => DashboardQuery.BuildAsync(db, now, ct), refresh, cancellationToken);

        // Tras «Actualizar» se regresa a la URL limpia: recargar la página no vuelve a forzar el cálculo.
        return refresh ? Redirect(Url.Content("~" + DashboardUrl)) : View(model);
    }
}
