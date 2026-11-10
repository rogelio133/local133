// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using Local133.Infrastructure.Identity;
using Local133.Web.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Local133.Web.Areas.Identity.Pages.Account.Manage;

public class ExternalLoginsModel(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager) : PageModel
{
    public IList<UserLoginInfo> CurrentLogins { get; set; }

    public IList<AuthenticationScheme> OtherLogins { get; set; }

    public bool ShowRemoveButton { get; set; }

    [TempData]
    public string StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound("No se encontró el usuario.");
        }

        CurrentLogins = await userManager.GetLoginsAsync(user);
        OtherLogins = (await signInManager.GetExternalAuthenticationSchemesAsync())
            .Where(auth => CurrentLogins.All(ul => auth.Name != ul.LoginProvider))
            .ToList();

        // Solo se puede quitar una cuenta externa si queda otra forma de entrar.
        ShowRemoveButton = await userManager.HasPasswordAsync(user) || CurrentLogins.Count > 1;
        return Page();
    }

    public async Task<IActionResult> OnPostRemoveLoginAsync(string loginProvider, string providerKey)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound("No se encontró el usuario.");
        }

        var logins = await userManager.GetLoginsAsync(user);
        if (!await userManager.HasPasswordAsync(user) && logins.Count <= 1)
        {
            StatusMessage = "Error: no puedes quitar tu única forma de entrar. Crea una contraseña primero.";
            return RedirectToPage();
        }

        var result = await userManager.RemoveLoginAsync(user, loginProvider, providerKey);
        if (!result.Succeeded)
        {
            StatusMessage = "Error: no se pudo quitar la cuenta externa.";
            return RedirectToPage();
        }

        await signInManager.RefreshSignInAsync(user);
        StatusMessage = "Se quitó la cuenta externa.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostLinkLoginAsync(string provider)
    {
        // Limpia la cookie externa para empezar un login limpio.
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        var redirectUrl = Url.Page("./ExternalLogins", pageHandler: "LinkLoginCallback");
        var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl, userManager.GetUserId(User));
        return new ChallengeResult(provider, properties);
    }

    public async Task<IActionResult> OnGetLinkLoginCallbackAsync(string remoteError = null)
    {
        if (remoteError != null)
        {
            StatusMessage = "Error: " + ExternalLoginErrors.Message(remoteError);
            return RedirectToPage();
        }

        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound("No se encontró el usuario.");
        }

        var userId = await userManager.GetUserIdAsync(user);
        var info = await signInManager.GetExternalLoginInfoAsync(userId);
        if (info == null)
        {
            StatusMessage = "Error: no se pudo leer la información del proveedor externo.";
            return RedirectToPage();
        }

        var result = await userManager.AddLoginAsync(user, info);
        if (!result.Succeeded)
        {
            StatusMessage = "Error: no se pudo ligar. Una cuenta externa solo puede estar ligada a un usuario.";
            return RedirectToPage();
        }

        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        StatusMessage = "Se ligó la cuenta externa.";
        return RedirectToPage();
    }
}
