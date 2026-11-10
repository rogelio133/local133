// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System.Text;
using Local133.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace Local133.Web.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class ConfirmEmailChangeModel(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager) : PageModel
{
    public string StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(string userId, string email, string code)
    {
        if (userId == null || email == null || code == null)
        {
            return LocalRedirect("~/");
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
        {
            StatusMessage = "Error: la liga no es válida.";
            return Page();
        }

        IdentityResult result;
        try
        {
            code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            result = await userManager.ChangeEmailAsync(user, email, code);
        }
        catch (FormatException)
        {
            result = IdentityResult.Failed();
        }
        if (!result.Succeeded)
        {
            StatusMessage = "Error: no se pudo cambiar el correo. La liga no es válida o ya expiró.";
            return Page();
        }

        // El correo también es el nombre de usuario.
        var setUserNameResult = await userManager.SetUserNameAsync(user, email);
        if (!setUserNameResult.Succeeded)
        {
            StatusMessage = "Error: no se pudo actualizar el nombre de usuario.";
            return Page();
        }

        await signInManager.RefreshSignInAsync(user);
        StatusMessage = "Listo, tu correo se cambió.";
        return Page();
    }
}
