using Local133.Application.Auth;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Controllers;

/// <summary>
/// Estatus de una venta para la vista del QR (respaldo de SignalR y confirmación del evento). El filtro multi-tenant
/// de EF Core hace que la venta de otro vendedor dé 404; sin sesión responde 401.
/// </summary>
[ApiController]
[Authorize(Roles = Roles.Merchant)]
[Route("api/sales")]
[ApiExplorerSettings(IgnoreApi = true)]
public class SalesApiController(AppDbContext db, TimeProvider clock) : ControllerBase
{
    /// <param name="PaidAtText">Hora del pago ya formateada en hora del centro de México (la misma que dibuja el servidor).</param>
    public sealed record SaleStatusResponse(int Id, string Status, DateTimeOffset? PaidAt, string? PaidAtText);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SaleStatusResponse>> Get(int id, CancellationToken cancellationToken)
    {
        var sale = await db.Sales.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (sale is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound);
        }

        // Estatus efectivo: una pendiente con el QR vencido ya se reporta vencida aunque el job no haya corrido.
        Response.Headers.CacheControl = "no-store";
        return new SaleStatusResponse(
            sale.Id,
            sale.EffectiveStatus(clock.GetUtcNow()).ToString(),
            sale.PaidAt,
            sale.PaidAt is { } paidAt ? SaleQrViewModel.FormatPaidAt(paidAt) : null);
    }
}
