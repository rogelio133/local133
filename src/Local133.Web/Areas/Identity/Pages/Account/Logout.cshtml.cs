// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using Local133.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Local133.Web.Areas.Identity.Pages.Account;

public class LogoutModel(SignInManager<ApplicationUser> signInManager, ILogger<LogoutModel> logger) : PageModel
{
    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(string returnUrl = null)
    {
        await signInManager.SignOutAsync();
        logger.LogInformation("Sesión cerrada.");
        if (returnUrl != null && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        // Redirige para que la vista se pinte ya sin la identidad del usuario.
        return RedirectToPage();
    }
}
