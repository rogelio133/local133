// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System.ComponentModel.DataAnnotations;
using System.Text;
using Local133.Infrastructure.Identity;
using Local133.Web.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace Local133.Web.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class ForgotPasswordModel(UserManager<ApplicationUser> userManager, IEmailSender emailSender) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "Escribe tu correo.")]
        [EmailAddress(ErrorMessage = "El correo no es válido.")]
        [Display(Name = "Correo")]
        public string Email { get; set; }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.FindByEmailAsync(Input.Email);
        if (user == null || !await userManager.IsEmailConfirmedAsync(user))
        {
            // No revelar si el usuario no existe o no está confirmado.
            return RedirectToPage("./ForgotPasswordConfirmation");
        }

        var code = await userManager.GeneratePasswordResetTokenAsync(user);
        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
        var callbackUrl = Url.Page(
            "/Account/ResetPassword",
            pageHandler: null,
            values: new { area = "Identity", code },
            protocol: Request.Scheme);

        await emailSender.SendEmailAsync(Input.Email, AccountEmails.ResetSubject, AccountEmails.ResetBody(callbackUrl));

        return RedirectToPage("./ForgotPasswordConfirmation");
    }
}
