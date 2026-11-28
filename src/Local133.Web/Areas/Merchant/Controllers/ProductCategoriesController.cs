using System.Security.Claims;
using Local133.Application.Auth;
using Local133.Application.Products;
using Local133.Application.Subscriptions;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Models;
using Local133.Web.Areas.Merchant.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Areas.Merchant.Controllers;

/// <summary>
/// Categorías del catálogo (/app/productos/categorias, ajustes 2026-11-26): agregar, renombrar, borrar (sus productos
/// quedan sin categoría) y ordenar, arrastrando (<c>product-categories.js</c> + SortableJS, que manda el orden completo
/// a <c>POST orden</c>) o con ↑/↓ (formularios que también funcionan sin JS). Mismos permisos que los productos.
/// </summary>
[Area("Merchant")]
[Authorize(Roles = Roles.Merchant)]
[Route("app/productos/categorias")]
[ApiExplorerSettings(IgnoreApi = true)]
public class ProductCategoriesController(AppDbContext db, IProductCategories categories, IPlanAccess plans) : Controller
{
    public const string NoticeKey = "CategoryNotice";

    public const string WarningKey = "CategoryWarning";

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await BuildAsync(new ProductCategoryInput(), cancellationToken));

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "Input")] ProductCategoryInput input, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            switch (await categories.CreateAsync(new CreateProductCategoryCommand(UserId, input.Name), cancellationToken))
            {
                case ProductCategoryResult.Saved saved:
                    TempData[NoticeKey] = $"Agregamos la categoría «{saved.Name}».";
                    return RedirectToAction(nameof(Index));
                case ProductCategoryResult.Invalid invalid:
                    ModelState.AddModelError($"Input.{nameof(ProductCategoryInput.Name)}", invalid.Message);
                    break;
                case var other:
                    return Denied(other);
            }
        }

        return View(nameof(Index), await BuildAsync(input, cancellationToken));
    }

    [HttpPost("{id:int}/renombrar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rename(int id, [Bind(Prefix = "Input")] ProductCategoryInput input, CancellationToken cancellationToken)
    {
        // El modal valida en el cliente; si llega inválido, el mensaje va arriba de la lista.
        switch (await categories.RenameAsync(new RenameProductCategoryCommand(UserId, id, input.Name), cancellationToken))
        {
            case ProductCategoryResult.Saved saved:
                TempData[NoticeKey] = $"La categoría ahora se llama «{saved.Name}».";
                return RedirectToAction(nameof(Index));
            case ProductCategoryResult.Invalid invalid:
                TempData[WarningKey] = invalid.Message;
                return RedirectToAction(nameof(Index));
            case var other:
                return Denied(other);
        }
    }

    [HttpPost("{id:int}/eliminar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        switch (await categories.DeleteAsync(new DeleteProductCategoryCommand(UserId, id), cancellationToken))
        {
            case ProductCategoryResult.Deleted deleted:
                TempData[NoticeKey] = deleted.ProductCount switch
                {
                    0 => $"Borramos la categoría «{deleted.Name}».",
                    1 => $"Borramos la categoría «{deleted.Name}». Su producto quedó sin categoría.",
                    var n => $"Borramos la categoría «{deleted.Name}». Sus {n} productos quedaron sin categoría.",
                };
                return RedirectToAction(nameof(Index));
            case var other:
                return Denied(other);
        }
    }

    [HttpPost("{id:int}/subir")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> MoveUp(int id, CancellationToken cancellationToken) => MoveAsync(id, -1, cancellationToken);

    [HttpPost("{id:int}/bajar")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> MoveDown(int id, CancellationToken cancellationToken) => MoveAsync(id, 1, cancellationToken);

    /// <summary>
    /// Orden completo desde <c>product-categories.js</c> (<c>ids</c> de la primera a la última). 204 si se guardó; 400 si
    /// no son exactamente las categorías del vendedor; 403 en solo lectura.
    /// </summary>
    [HttpPost("orden")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reorder([FromForm(Name = "ids")] List<int>? ids, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest();
        }

        return await categories.ReorderAsync(new ReorderProductCategoriesCommand(UserId, ids ?? []), cancellationToken) switch
        {
            ProductCategoryResult.Reordered => NoContent(),
            ProductCategoryResult.Invalid => BadRequest(),
            ProductCategoryResult.NotFound => NotFound(),
            _ => StatusCode(StatusCodes.Status403Forbidden),
        };
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private async Task<IActionResult> MoveAsync(int id, int offset, CancellationToken cancellationToken) =>
        await categories.MoveAsync(new MoveProductCategoryCommand(UserId, id, offset), cancellationToken) switch
        {
            ProductCategoryResult.Reordered => RedirectToAction(nameof(Index)),
            var other => Denied(other),
        };

    private async Task<ProductCategoriesViewModel> BuildAsync(ProductCategoryInput input, CancellationToken cancellationToken)
    {
        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        var plan = await plans.GetForMerchantAsync(merchant.Id, cancellationToken);

        // El filtro multi-tenant ya limita al vendedor de la sesión; el MerchantId explícito es la consulta en sí.
        var rows = await db.ProductCategories.AsNoTracking()
            .Where(c => c.MerchantId == merchant.Id)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ThenBy(c => c.Id)
            .Select(c => new ProductCategoryRow(
                c.Id,
                c.Name,
                db.Products.Count(p => p.MerchantId == merchant.Id && p.CategoryId == c.Id && !p.IsDeleted)))
            .ToListAsync(cancellationToken);

        return new ProductCategoriesViewModel
        {
            Items = rows,
            Input = input,
            ReadOnlyReason = ProductListQuery.ReadOnlyReasonFor(merchant, plan),
        };
    }

    /// <summary>No existe (404), dado de baja o plan de solo lectura (de vuelta a la lista con el aviso).</summary>
    private IActionResult Denied(ProductCategoryResult result)
    {
        switch (result)
        {
            case ProductCategoryResult.NotFound:
                return NotFound();
            case ProductCategoryResult.NotAllowed:
                TempData[WarningKey] = ProductMessages.NotAllowed;
                break;
            case ProductCategoryResult.PlanRequired:
                TempData[WarningKey] = ProductMessages.PlanRequired;
                break;
            case ProductCategoryResult.Invalid invalid:
                TempData[WarningKey] = invalid.Message;
                break;
        }

        return RedirectToAction(nameof(Index));
    }
}
