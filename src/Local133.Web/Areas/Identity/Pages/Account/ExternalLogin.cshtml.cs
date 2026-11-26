// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using Local133.Domain.Merchants;
using Local133.Infrastructure.Identity;
using Local133.Web.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace Local133.Web.Areas.Identity.Pages.Account;

/// <summary>
/// Login y alta con Google/Facebook. Reglas: si se conserva el correo que manda el proveedor, se da por
/// confirmado; si ya existe una cuenta con ese correo no se liga sola (se entra con contraseña y se liga
/// desde Manage/ExternalLogins).
/// </summary>
[AllowAnonymous]
[SignedInAntiforgeryRedirect]
public class ExternalLoginModel(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    AccountRegistration registration,
    TimeProvider clock,
    ILogger<ExternalLoginModel> logger,
    IEmailSender emailSender) : PageModel
{
    public const string ExistingAccountMessage =
        "Ya tienes una cuenta con ese correo. Entra con tu contraseña y liga {0} desde Mi cuenta → Cuentas externas.";

    [BindProperty]
    public InputModel Input { get; set; }

    public string ProviderDisplayName { get; set; }

    public string ReturnUrl { get; set; }

    [TempData]
    public string ErrorMessage { get; set; }

    public class InputModel : MerchantSignUpInput
    {
        [Required(ErrorMessage = "Escribe tu correo.")]
        [EmailAddress(ErrorMessage = "El correo no es válido.")]
        [Display(Name = "Correo")]
        public string Email { get; set; }
    }

    public IActionResult OnGet() => RedirectToPage("./Login");

    public IActionResult OnPost(string provider, string returnUrl = null)
    {
        // Redirige al proveedor externo.
        var redirectUrl = Url.Page("./ExternalLogin", pageHandler: "Callback", values: new { returnUrl });
        var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return new ChallengeResult(provider, properties);
    }

    public async Task<IActionResult> OnGetCallbackAsync(string returnUrl = null, string remoteError = null)
    {
        if (remoteError != null)
        {
            ErrorMessage = ExternalLoginErrors.Message(remoteError);
            return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
        }

        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            ErrorMessage = "No se pudo leer la información del proveedor externo.";
            return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
        }

        // Si ya tiene la cuenta externa ligada, entra directo.
        var result = await signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);
        if (result.Succeeded)
        {
            logger.LogInformation("Inicio de sesión con {LoginProvider}.", info.LoginProvider);
            return await RedirectAfterLoginAsync(returnUrl, await userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey));
        }
        if (result.IsLockedOut)
        {
            return RedirectToPage("./Lockout");
        }
        if (result.IsNotAllowed)
        {
            ErrorMessage = "Confirma tu correo antes de entrar. Revisa tu bandeja o reenvía la confirmación.";
            return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
        }

        // No está ligada. Si el correo ya es de otra cuenta, no se liga sola.
        var providerEmail = ProviderEmail(info);
        if (providerEmail != null && await userManager.FindByEmailAsync(providerEmail) != null)
        {
            ErrorMessage = string.Format(ExistingAccountMessage, info.ProviderDisplayName);
            return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
        }

        // No tiene cuenta: se le pide confirmar el correo para crearla.
        ReturnUrl = returnUrl;
        ProviderDisplayName = info.ProviderDisplayName;
        Input = new InputModel
        {
            Email = providerEmail,
            OwnerName = ProviderName(info),
            ReferralCode = ReferralCookie.Read(Request), // de la liga /r/{código} (5.8)
        };
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmationAsync(string returnUrl = null)
    {
        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            ErrorMessage = "No se pudo leer la información del proveedor externo.";
            return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
        }

        if (ModelState.IsValid && await userManager.FindByEmailAsync(Input.Email) != null)
        {
            ModelState.AddModelError(string.Empty, string.Format(ExistingAccountMessage, info.ProviderDisplayName));
        }

        if (ModelState.IsValid)
        {
            // El correo que manda el proveedor ya lo verificó él; si el usuario lo cambia, se confirma por correo.
            var emailConfirmed = string.Equals(Input.Email, ProviderEmail(info), StringComparison.OrdinalIgnoreCase);
            var user = new ApplicationUser { UserName = Input.Email, Email = Input.Email, EmailConfirmed = emailConfirmed }
                .AcceptLegalDocuments(clock.GetUtcNow());
            var result = await registration.CreateMerchantAsync(user, Input.OwnerName, password: null, info, Input.ReferralCode);
            if (result.Succeeded)
            {
                ReferralCookie.Delete(Response);
                logger.LogInformation("Usuario {UserId} creado con {LoginProvider}.", user.Id, info.LoginProvider);

                if (emailConfirmed)
                {
                    await signInManager.SignInAsync(user, isPersistent: false, info.LoginProvider);
                    return await RedirectAfterLoginAsync(returnUrl, user);
                }

                var userId = await userManager.GetUserIdAsync(user);
                var code = await userManager.GenerateEmailConfirmationTokenAsync(user);
                code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                var callbackUrl = Url.Page(
                    "/Account/ConfirmEmail",
                    pageHandler: null,
                    values: new { area = "Identity", userId, code, returnUrl },
                    protocol: Request.Scheme);
                await emailSender.SendEmailAsync(Input.Email, AccountEmails.ConfirmSubject, AccountEmails.ConfirmBody(callbackUrl, PersonName.FirstName(Input.OwnerName)));

                await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                return RedirectToPage("./RegisterConfirmation", new { Input.Email, returnUrl });
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(
                    error.Code == ReferralCodeMessages.UnknownCodeError ? $"{nameof(Input)}.{nameof(Input.ReferralCode)}" : string.Empty,
                    error.Description);
            }
        }

        ProviderDisplayName = info.ProviderDisplayName;
        ReturnUrl = returnUrl;
        return Page();
    }

    /// <summary>
    /// Nombre que manda el proveedor, para proponerlo en el alta: el nombre completo o, sin él, nombre y apellido.
    /// Limpio con <see cref="PersonName.Clean"/> (solo letras y espacios: «O'Brien» ⇒ «O Brien») y recortado.
    /// </summary>
    private static string ProviderName(ExternalLoginInfo info)
    {
        var name = info.Principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"{info.Principal.FindFirstValue(ClaimTypes.GivenName)} {info.Principal.FindFirstValue(ClaimTypes.Surname)}";
        }

        name = PersonName.Clean(name);
        return name.Length == 0 ? null : name[..Math.Min(name.Length, PersonName.MaxLength)].TrimEnd();
    }

    private static string ProviderEmail(ExternalLoginInfo info) =>
        info.Principal.FindFirstValue(ClaimTypes.Email) is { Length: > 0 } email ? email : null;

    private async Task<IActionResult> RedirectAfterLoginAsync(string returnUrl, ApplicationUser user)
    {
        // Ya no se necesita la cookie del proveedor.
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        return LocalRedirect(await PostLoginRedirect.ResolveAsync(userManager, user, returnUrl, Url.IsLocalUrl));
    }
}
