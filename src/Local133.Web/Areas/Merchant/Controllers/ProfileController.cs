using Local133.Application.Auth;
using Local133.Application.Merchants;
using Local133.Application.Storage;
using Local133.Infrastructure.Identity;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Local133.Web.Areas.Merchant.Controllers;

/// <summary>
/// «Mi información» (/app/mi-informacion, 4.1): datos generales (tu nombre, el del negocio, el teléfono, que es el
/// celular de la cuenta, y la dirección), el correo en solo lectura, el logo y la foto del local. Cada imagen tiene su
/// propio formulario. Todo es del vendedor de la sesión: no hay ids en las rutas.
/// </summary>
[Area("Merchant")]
[Authorize(Roles = Roles.Merchant)]
[Route("app/mi-informacion")]
[ApiExplorerSettings(IgnoreApi = true)]
public class ProfileController(
    AppDbContext db,
    IMerchantProfile profile,
    IFileStorage storage,
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn) : Controller
{
    /// <summary>Mensaje de éxito (PRG).</summary>
    public const string NoticeKey = "ProfileNotice";

    /// <summary>Aviso (cuenta dada de baja).</summary>
    public const string WarningKey = "ProfileWarning";

    /// <summary>Las imágenes no cambian: cada una nueva tiene otra URL (<c>?v=</c>).</summary>
    private const string ImageCacheControl = "private, max-age=31536000, immutable";

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(ProfileViewModel.From(await LoadAsync(cancellationToken)));

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([Bind(Prefix = "Input")] ProfileInput input, CancellationToken cancellationToken)
    {
        var data = await LoadAsync(cancellationToken);
        if (ModelState.IsValid)
        {
            var command = new SaveProfileCommand(data.MerchantId, input.OwnerName, input.BusinessName, input.Address, input.PhoneNumber);
            switch (await profile.SaveAsync(command, cancellationToken))
            {
                case SaveProfileResult.Saved saved:
                    if (saved.PhoneChanged && await users.GetUserAsync(User) is { } user)
                    {
                        // El celular renueva el security stamp: sin esto, la sesión se cerraría al revalidarse.
                        await signIn.RefreshSignInAsync(user);
                    }

                    TempData[NoticeKey] = ProfileMessages.Saved;
                    return RedirectToAction(nameof(Index));

                case SaveProfileResult.ReadOnly:
                    return ReadOnlyRedirect();

                case SaveProfileResult.NotFound:
                    return NotFound();

                case SaveProfileResult.Invalid invalid:
                    foreach (var (field, messages) in invalid.Errors)
                    {
                        foreach (var message in messages)
                        {
                            ModelState.AddModelError($"Input.{field}", message);
                        }
                    }

                    break;
            }
        }

        return View(nameof(Index), ProfileViewModel.From(data, input));
    }

    [HttpPost("logo")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MerchantImageRules.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MerchantImageRules.MaxRequestBytes)]
    public Task<IActionResult> SaveLogo(IFormFile? image, CancellationToken cancellationToken) =>
        SaveImageAsync(MerchantImageKind.Logo, image, cancellationToken);

    [HttpPost("logo/quitar")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> RemoveLogo(CancellationToken cancellationToken) =>
        RemoveImageAsync(MerchantImageKind.Logo, cancellationToken);

    [HttpPost("foto")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MerchantImageRules.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MerchantImageRules.MaxRequestBytes)]
    public Task<IActionResult> SaveShopPhoto(IFormFile? image, CancellationToken cancellationToken) =>
        SaveImageAsync(MerchantImageKind.ShopPhoto, image, cancellationToken);

    [HttpPost("foto/quitar")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> RemoveShopPhoto(CancellationToken cancellationToken) =>
        RemoveImageAsync(MerchantImageKind.ShopPhoto, cancellationToken);

    [HttpGet("logo")]
    public Task<IActionResult> Logo(CancellationToken cancellationToken) =>
        ImageFileAsync(MerchantImageKind.Logo, cancellationToken);

    [HttpGet("foto")]
    public Task<IActionResult> ShopPhoto(CancellationToken cancellationToken) =>
        ImageFileAsync(MerchantImageKind.ShopPhoto, cancellationToken);

    private async Task<IActionResult> SaveImageAsync(MerchantImageKind kind, IFormFile? image, CancellationToken cancellationToken)
    {
        var data = await LoadAsync(cancellationToken);
        string error;
        if (image is null || image.Length == 0)
        {
            error = image is null ? MerchantImageMessages.Required : MerchantImageMessages.Empty;
        }
        else
        {
            await using var stream = image.OpenReadStream();
            switch (await profile.SaveImageAsync(data.MerchantId, kind, new MerchantImageUpload(stream, image.Length), cancellationToken))
            {
                case SaveMerchantImageResult.Saved:
                    TempData[NoticeKey] = kind == MerchantImageKind.Logo ? ProfileMessages.LogoSaved : ProfileMessages.ShopPhotoSaved;
                    return RedirectToAction(nameof(Index));
                case SaveMerchantImageResult.ReadOnly:
                    return ReadOnlyRedirect();
                case SaveMerchantImageResult.NotFound:
                    return NotFound();
                case SaveMerchantImageResult.Invalid invalid:
                    error = invalid.Message;
                    break;
                default:
                    throw new InvalidOperationException("Resultado de imagen desconocido.");
            }
        }

        var model = ProfileViewModel.From(data);
        return View(nameof(Index), kind == MerchantImageKind.Logo
            ? model with { LogoError = error }
            : model with { ShopPhotoError = error });
    }

    private async Task<IActionResult> RemoveImageAsync(MerchantImageKind kind, CancellationToken cancellationToken)
    {
        var data = await LoadAsync(cancellationToken);
        switch (await profile.RemoveImageAsync(data.MerchantId, kind, cancellationToken))
        {
            case SaveMerchantImageResult.ReadOnly:
                return ReadOnlyRedirect();
            case SaveMerchantImageResult.NotFound:
                return NotFound();
            default:
                TempData[NoticeKey] = kind == MerchantImageKind.Logo ? ProfileMessages.LogoRemoved : ProfileMessages.ShopPhotoRemoved;
                return RedirectToAction(nameof(Index));
        }
    }

    private async Task<IActionResult> ImageFileAsync(MerchantImageKind kind, CancellationToken cancellationToken)
    {
        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        var key = kind == MerchantImageKind.Logo ? merchant.LogoKey : merchant.ShopPhotoKey;
        if (key is null)
        {
            return NotFound();
        }

        var stream = await storage.OpenReadAsync(MerchantImageRules.FileKey(kind, key), cancellationToken);
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = ImageCacheControl;
        return File(stream, MerchantImageRules.ContentType(kind));
    }

    private async Task<MerchantProfileData> LoadAsync(CancellationToken cancellationToken)
    {
        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        return await profile.GetAsync(merchant.Id, cancellationToken)
            ?? throw new InvalidOperationException($"No se encontró el vendedor {merchant.Id}.");
    }

    private RedirectToActionResult ReadOnlyRedirect()
    {
        TempData[WarningKey] = ProfileMessages.ReadOnly;
        return RedirectToAction(nameof(Index));
    }
}
