using System.Security.Claims;
using Local133.Application.Auth;
using Local133.Application.Products;
using Local133.Application.Storage;
using Local133.Application.Subscriptions;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Models;
using Local133.Web.Areas.Merchant.Services;
using Local133.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Areas.Merchant.Controllers;

/// <summary>
/// Productos del vendedor (/app/productos): lista paginada con búsqueda, alta y edición con foto (cámara o
/// galería), y baja lógica. El filtro multi-tenant de EF Core hace que el producto de otro vendedor dé 404.
/// </summary>
[Area("Merchant")]
[Authorize(Roles = Roles.Merchant)]
[Route("app/productos")]
[ApiExplorerSettings(IgnoreApi = true)]
public class ProductsController(AppDbContext db, IProductCatalog catalog, IFileStorage storage, IPlanAccess plans) : Controller
{
    /// <summary>Mensaje de éxito (PRG) que muestra la lista.</summary>
    public const string NoticeKey = "ProductNotice";

    /// <summary>Aviso (p. ej. cuenta dada de baja) que muestra la lista.</summary>
    public const string WarningKey = "ProductWarning";

    /// <summary>Las fotos no cambian: cada foto nueva tiene otra URL (<c>?v=</c>).</summary>
    private const string PhotoCacheControl = "private, max-age=31536000, immutable";

    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery(Name = "q")] string? q, [FromQuery(Name = PageInfo.QueryKey)] int? page, CancellationToken cancellationToken) =>
        View(await BuildListAsync(q, page, cancellationToken));

    /// <summary>
    /// Fragmento con los resultados (conteo, lista y paginación) para la búsqueda asíncrona de
    /// <c>product-search.js</c>. Ruta propia (no la de la lista con otro encabezado) para que el navegador nunca guarde
    /// ni restaure al volver el fragmento en lugar de la página completa.
    /// </summary>
    [HttpGet("resultados")]
    public async Task<IActionResult> Results(
        [FromQuery(Name = "q")] string? q, [FromQuery(Name = PageInfo.QueryKey)] int? page, CancellationToken cancellationToken)
    {
        var model = await BuildListAsync(q, page, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return PartialView("_ProductResults", model);
    }

    [HttpGet("nuevo")]
    public async Task<IActionResult> New(CancellationToken cancellationToken)
    {
        if (await ReadOnlyRedirectAsync(cancellationToken) is { } readOnly)
        {
            return readOnly;
        }

        return View("Form", new ProductFormViewModel());
    }

    /// <summary>Lista paginada con la búsqueda por nombre (recortada a <see cref="ProductListViewModel.QueryMaxLength"/>).</summary>
    private async Task<ProductListViewModel> BuildListAsync(string? q, int? page, CancellationToken cancellationToken)
    {
        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        var plan = await plans.GetForMerchantAsync(merchant.Id, cancellationToken);
        return await ProductListQuery.BuildAsync(
            db, merchant, q, page, Url, ProductUrls.List, ProductListQuery.ReadOnlyReasonFor(merchant, plan), cancellationToken);
    }

    /// <summary>
    /// Formularios de alta y edición: un vendedor dado de baja, en Básico o vencido (catálogo de solo lectura) vuelve a
    /// la lista con el aviso. El caso de uso lo valida de nuevo al guardar.
    /// </summary>
    private async Task<IActionResult?> ReadOnlyRedirectAsync(CancellationToken cancellationToken)
    {
        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        if (!merchant.CanManageProducts)
        {
            return NotAllowed();
        }

        return (await plans.GetForMerchantAsync(merchant.Id, cancellationToken)).Entitlements.CanEditCatalog ? null : PlanRequired();
    }

    [HttpPost("nuevo")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(ProductPhotoRules.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = ProductPhotoRules.MaxRequestBytes)]
    public Task<IActionResult> Create([Bind(Prefix = "Input")] ProductInput input, CancellationToken cancellationToken) =>
        SaveAsync(null, input, cancellationToken);

    [HttpGet("{id:int}/editar")]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        if (await ReadOnlyRedirectAsync(cancellationToken) is { } readOnly)
        {
            return readOnly;
        }

        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        return View("Form", new ProductFormViewModel
        {
            ProductId = product.Id,
            CurrentPhotoUrl = PhotoUrl(product.Id, product.PhotoKey),
            Input = new ProductInput
            {
                Name = product.Name,
                Price = ProductInput.FormatPrice(product.Price),
                Description = product.Description,
            },
        });
    }

    [HttpPost("{id:int}/editar")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(ProductPhotoRules.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = ProductPhotoRules.MaxRequestBytes)]
    public Task<IActionResult> Update(int id, [Bind(Prefix = "Input")] ProductInput input, CancellationToken cancellationToken) =>
        SaveAsync(id, input, cancellationToken);

    /// <summary>Baja lógica (la fila y la foto se conservan; las ventas ya guardan su copia del nombre y el precio).</summary>
    [HttpPost("{id:int}/eliminar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var name = await db.Products.AsNoTracking().Where(p => p.Id == id && !p.IsDeleted).Select(p => p.Name)
            .SingleOrDefaultAsync(cancellationToken);
        switch (await catalog.DeleteAsync(new DeleteProductCommand(UserId, id), cancellationToken))
        {
            case DeleteProductResult.NotFound:
                return NotFound();
            case DeleteProductResult.NotAllowed:
                return NotAllowed();
            case DeleteProductResult.PlanRequired:
                return PlanRequired();
            default:
                TempData[NoticeKey] = $"Eliminamos «{name}» de tus productos.";
                return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet("{id:int}/foto")]
    public Task<IActionResult> Photo(int id, CancellationToken cancellationToken) =>
        PhotoFileAsync(id, ProductPhotoRules.PhotoFileKey, cancellationToken);

    [HttpGet("{id:int}/foto/miniatura")]
    public Task<IActionResult> Thumbnail(int id, CancellationToken cancellationToken) =>
        PhotoFileAsync(id, ProductPhotoRules.ThumbnailFileKey, cancellationToken);

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private async Task<IActionResult> SaveAsync(int? id, ProductInput input, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            if (!input.TryGetPrice(out var price))
            {
                ModelState.AddModelError($"Input.{nameof(ProductInput.Price)}", ProductMessages.PriceInvalid);
            }
            else
            {
                await using var photoStream = input.Photo?.OpenReadStream();
                var photo = input.Photo is { } file ? new ProductPhotoUpload(photoStream!, file.Length) : null;
                var command = new SaveProductCommand(UserId, id, input.Name, input.Description, price, photo, input.RemovePhoto);
                switch (await catalog.SaveAsync(command, cancellationToken))
                {
                    case SaveProductResult.Saved:
                        TempData[NoticeKey] = id is null
                            ? $"Agregamos «{input.Name!.Trim()}» a tus productos."
                            : $"Guardamos los cambios de «{input.Name!.Trim()}».";
                        return RedirectToAction(nameof(Index));

                    case SaveProductResult.NotFound:
                        return NotFound();

                    case SaveProductResult.NotAllowed:
                        return NotAllowed();

                    case SaveProductResult.PlanRequired:
                        return PlanRequired();

                    case SaveProductResult.Invalid invalid:
                        foreach (var (field, messages) in invalid.Errors)
                        {
                            var key = field switch
                            {
                                nameof(SaveProductCommand.Name) => $"Input.{nameof(ProductInput.Name)}",
                                nameof(SaveProductCommand.Description) => $"Input.{nameof(ProductInput.Description)}",
                                nameof(SaveProductCommand.Price) => $"Input.{nameof(ProductInput.Price)}",
                                nameof(SaveProductCommand.Photo) => $"Input.{nameof(ProductInput.Photo)}",
                                _ => string.Empty,
                            };
                            foreach (var message in messages)
                            {
                                ModelState.AddModelError(key, message);
                            }
                        }
                        break;
                }
            }
        }

        // De vuelta al formulario con los errores.
        string? currentPhotoUrl = null;
        if (id is { } productId)
        {
            var product = await db.Products.AsNoTracking().Where(p => p.Id == productId && !p.IsDeleted)
                .Select(p => new { p.Id, p.PhotoKey }).SingleOrDefaultAsync(cancellationToken);
            if (product is null)
            {
                return NotFound();
            }

            currentPhotoUrl = input.RemovePhoto ? null : PhotoUrl(product.Id, product.PhotoKey);
        }

        return View("Form", new ProductFormViewModel
        {
            ProductId = id,
            Input = input,
            CurrentPhotoUrl = currentPhotoUrl,
            // Solo si la foto estaba bien y falló otro campo (si la foto es el error, su mensaje ya lo dice).
            PhotoNeedsReselect = input.Photo is not null
                && ModelState[$"Input.{nameof(ProductInput.Photo)}"] is not { Errors.Count: > 0 },
        });
    }

    private async Task<IActionResult> PhotoFileAsync(int id, Func<string, string> fileKey, CancellationToken cancellationToken)
    {
        var photoKey = await db.Products.AsNoTracking().Where(p => p.Id == id && !p.IsDeleted).Select(p => p.PhotoKey)
            .SingleOrDefaultAsync(cancellationToken);
        if (photoKey is null)
        {
            return NotFound();
        }

        var stream = await storage.OpenReadAsync(fileKey(photoKey), cancellationToken);
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = PhotoCacheControl;
        return File(stream, ProductPhotoRules.StoredContentType);
    }

    private string? PhotoUrl(int id, string? photoKey) =>
        photoKey is null ? null : Url.Content($"~{ProductUrls.Photo(id, photoKey)}");

    /// <summary>Vendedor dado de baja: vuelve a la lista con el aviso.</summary>
    private RedirectToActionResult NotAllowed()
    {
        TempData[WarningKey] = ProductMessages.NotAllowed;
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Básico o vencido (catálogo de solo lectura): vuelve a la lista con el aviso.</summary>
    private RedirectToActionResult PlanRequired()
    {
        TempData[WarningKey] = ProductMessages.PlanRequired;
        return RedirectToAction(nameof(Index));
    }
}
