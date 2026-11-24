using Local133.Application.Auth;
using Local133.Application.Merchants;
using Local133.Application.Subscriptions;
using Local133.Domain.Merchants;
using Local133.Domain.Sales;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Models;
using Local133.Web.Hosting;
using Local133.Web.Models;
using Local133.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Local133.Web.Areas.Merchant.Controllers;

/// <summary>
/// «Catálogo en línea» (/app/tienda, 5.10): el vendedor elige la liga de su catálogo público (<c>/tienda/{slug}</c>),
/// lo publica o deja de publicarlo, lo comparte y ve la vista previa. Publicar es de Pro; en el gratuito se muestra el
/// aviso de Pro. Un vendedor dado de baja solo puede leer.
/// </summary>
[Area("Merchant")]
[Authorize(Roles = Roles.Merchant)]
[Route("app/tienda")]
[ApiExplorerSettings(IgnoreApi = true)]
public class StorefrontSettingsController(
    AppDbContext db,
    IPublicCatalogSettings catalog,
    IPlanAccess plans,
    IOptions<PublicSiteOptions> site) : Controller
{
    /// <summary>Mensaje de éxito (PRG).</summary>
    public const string NoticeKey = "StorefrontNotice";

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        return View(await BuildAsync(merchant, null, cancellationToken));
    }

    [HttpPost("publicar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish([Bind(Prefix = "Input")] StorefrontSlugInput input, CancellationToken cancellationToken)
    {
        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        // El servidor normaliza: «Tacos Don Ramón» llega como «tacos-don-ramon» aunque falle el JS.
        var normalized = PublicSlug.Normalize(input.Slug);
        ModelState.Clear();
        if (!PublicSlug.IsValid(normalized))
        {
            ModelState.AddModelError("Input.Slug", normalized.Length == 0 ? PublicCatalogMessages.SlugRequired : PublicCatalogMessages.InvalidSlug);
        }
        else
        {
            switch (await catalog.PublishAsync(merchant.Id, normalized, cancellationToken))
            {
                case PublishCatalogResult.Published:
                    TempData[NoticeKey] = PublicCatalogMessages.Published;
                    return RedirectToAction(nameof(Index));
                case PublishCatalogResult.SlugTaken:
                    ModelState.AddModelError("Input.Slug", PublicCatalogMessages.SlugTaken);
                    break;
                case PublishCatalogResult.InvalidSlug:
                    ModelState.AddModelError("Input.Slug", PublicCatalogMessages.InvalidSlug);
                    break;
                case PublishCatalogResult.PlanRequired:
                    ModelState.AddModelError(string.Empty, PublicCatalogMessages.PlanRequired);
                    break;
                case PublishCatalogResult.BusinessNameRequired:
                    ModelState.AddModelError(string.Empty, PublicCatalogMessages.BusinessNameRequired);
                    break;
                default:
                    ModelState.AddModelError(string.Empty, PublicCatalogMessages.NotAllowed);
                    break;
            }
        }

        // El caso de uso pudo dejar al vendedor modificado en el contexto (slug tomado): se lee de nuevo.
        db.ChangeTracker.Clear();
        merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        Response.StatusCode = StatusCodes.Status400BadRequest;
        var shown = new StorefrontSlugInput { Slug = normalized.Length > 0 ? normalized : input.Slug };
        return View(nameof(Index), await BuildAsync(merchant, shown, cancellationToken));
    }

    [HttpPost("despublicar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unpublish(CancellationToken cancellationToken)
    {
        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        if (await catalog.UnpublishAsync(merchant.Id, cancellationToken) is UnpublishCatalogResult.Unpublished)
        {
            TempData[NoticeKey] = PublicCatalogMessages.Unpublished;
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Vista previa: el catálogo como lo verán sus clientes, con las imágenes por las rutas privadas. Se ve aunque no
    /// esté publicado y en cualquier plan, porque solo la ve el vendedor.
    /// </summary>
    [HttpGet("vista-previa")]
    public async Task<IActionResult> Preview(
        string? q, [FromQuery(Name = PageInfo.QueryKey)] int? page, CancellationToken cancellationToken)
    {
        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        var model = await StorefrontQuery.BuildAsync(
            db, merchant, q, page, StorefrontQuery.PrivateImages, StorefrontSettingsUrls.Preview,
            site.Value.LandingUrlFor(merchant.ReferralCode), isPreview: true, cancellationToken);
        return View("~/Views/Storefront/Show.cshtml", model);
    }

    private async Task<StorefrontSettingsViewModel> BuildAsync(
        Domain.Merchants.Merchant merchant, StorefrontSlugInput? input, CancellationToken cancellationToken)
    {
        var plan = await plans.GetForMerchantAsync(merchant.Id, cancellationToken);
        var phone = await db.Users.AsNoTracking()
            .Where(u => u.Id == merchant.UserId)
            .Select(u => u.PhoneNumber)
            .SingleOrDefaultAsync(cancellationToken);
        var productCount = await db.Products.AsNoTracking()
            .CountAsync(p => p.MerchantId == merchant.Id && !p.IsDeleted, cancellationToken);
        var baseUrl = site.Value.BaseUrlFor(Request);
        var canPublish = plan.Entitlements.CanPublishCatalog;
        var businessName = string.IsNullOrWhiteSpace(merchant.BusinessName) ? null : merchant.BusinessName.Trim();
        var url = merchant.PublicCatalogEnabled && merchant.PublicSlug is { } slug ? baseUrl + PublicSlug.PathFor(slug) : null;

        return new StorefrontSettingsViewModel
        {
            Input = input ?? new StorefrontSlugInput { Slug = merchant.PublicSlug ?? PublicSlug.Suggest(businessName) },
            UrlPrefix = baseUrl + PublicSlug.PathPrefix,
            CanPublish = canPublish,
            IsDisabled = merchant.Status == MerchantStatus.Disabled,
            IsSuspended = merchant.Status == MerchantStatus.Suspended,
            HasBusinessName = businessName is not null,
            IsPublished = merchant.PublicCatalogEnabled,
            Url = url,
            MaskedPhone = PhoneNumber.IsValid(phone) ? PhoneNumber.Mask(phone!) : null,
            ProductCount = productCount,
            ProRequired = canPublish ? null : new ProRequiredViewModel(
                "Comparte tu catálogo con una liga",
                "Con Pro tus clientes ven tus productos, precios y fotos en una página, y te piden por WhatsApp.",
                ProductListViewModel.WaitingCatalogText(productCount),
                "bi-shop"),
            ShareMessage = url is not null && businessName is not null
                ? StorefrontSettingsViewModel.MessageFor(businessName, url)
                : null,
        };
    }
}
