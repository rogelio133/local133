// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using Local133.Application.Auth;
using Local133.Infrastructure.Identity;
using Local133.Web.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace Local133.Web.Areas.Identity.Pages.Account;

/// <summary>Login con proveedores externos (Google y Facebook se configuran en el paso 1.3).</summary>
[AllowAnonymous]
public class ExternalLoginModel(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    ILogger<ExternalLoginModel> logger,
    IEmailSender emailSender) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; }

    public string ProviderDisplayName { get; set; }

    public string ReturnUrl { get; set; }

    [TempData]
    public string ErrorMessage { get; set; }

    public class InputModel
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
            ErrorMessage = $"El proveedor externo respondió con un error: {remoteError}";
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

        // No tiene cuenta: se le pide confirmar el correo para crearla.
        ReturnUrl = returnUrl;
        ProviderDisplayName = info.ProviderDisplayName;
        if (info.Principal.HasClaim(c => c.Type == ClaimTypes.Email))
        {
            Input = new InputModel { Email = info.Principal.FindFirstValue(ClaimTypes.Email) };
        }
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

        if (ModelState.IsValid)
        {
            var user = new ApplicationUser { UserName = Input.Email, Email = Input.Email };
            var result = await userManager.CreateAsync(user);
            if (result.Succeeded)
            {
                result = await userManager.AddToRoleAsync(user, Roles.Merchant);
            }
            if (result.Succeeded)
            {
                result = await userManager.AddLoginAsync(user, info);
            }
            if (result.Succeeded)
            {
                logger.LogInformation("Usuario {UserId} creado con {LoginProvider}.", user.Id, info.LoginProvider);

                var userId = await userManager.GetUserIdAsync(user);
                var code = await userManager.GenerateEmailConfirmationTokenAsync(user);
                code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                var callbackUrl = Url.Page(
                    "/Account/ConfirmEmail",
                    pageHandler: null,
                    values: new { area = "Identity", userId, code, returnUrl },
                    protocol: Request.Scheme);
                await emailSender.SendEmailAsync(Input.Email, AccountEmails.ConfirmSubject, AccountEmails.ConfirmBody(callbackUrl));

                if (userManager.Options.SignIn.RequireConfirmedAccount)
                {
                    return RedirectToPage("./RegisterConfirmation", new { Input.Email, returnUrl });
                }

                await signInManager.SignInAsync(user, isPersistent: false, info.LoginProvider);
                return await RedirectAfterLoginAsync(returnUrl, user);
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }

        ProviderDisplayName = info.ProviderDisplayName;
        ReturnUrl = returnUrl;
        return Page();
    }

    private async Task<IActionResult> RedirectAfterLoginAsync(string returnUrl, ApplicationUser user)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }
        return LocalRedirect(await PostLoginRedirect.HomeUrlAsync(userManager, user));
    }
}
