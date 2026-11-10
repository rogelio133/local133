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
public class ConfirmEmailModel(UserManager<ApplicationUser> userManager) : PageModel
{
    public string StatusMessage { get; set; }

    public bool Succeeded { get; set; }

    public string ReturnUrl { get; set; }

    public async Task<IActionResult> OnGetAsync(string userId, string code, string returnUrl = null)
    {
        if (userId == null || code == null)
        {
            return LocalRedirect("~/");
        }

        ReturnUrl = returnUrl;
        var user = await userManager.FindByIdAsync(userId);
        IdentityResult result;
        if (user == null)
        {
            result = IdentityResult.Failed();
        }
        else
        {
            try
            {
                code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
                result = await userManager.ConfirmEmailAsync(user, code);
            }
            catch (FormatException)
            {
                result = IdentityResult.Failed();
            }
        }

        Succeeded = result.Succeeded;
        StatusMessage = result.Succeeded
            ? "¡Listo! Tu correo quedó confirmado. Ya puedes entrar."
            : "Error: la liga no es válida o ya expiró.";
        return Page();
    }
}
