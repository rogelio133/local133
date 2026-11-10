// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System.ComponentModel.DataAnnotations;
using System.Text;
using Local133.Infrastructure.Identity;
using Local133.Web.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace Local133.Web.Areas.Identity.Pages.Account.Manage;

public class EmailModel(UserManager<ApplicationUser> userManager, IEmailSender emailSender) : PageModel
{
    [Display(Name = "Correo actual")]
    public string Email { get; set; }

    public bool IsEmailConfirmed { get; set; }

    [TempData]
    public string StatusMessage { get; set; }

    [BindProperty]
    public InputModel Input { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "Escribe el nuevo correo.")]
        [EmailAddress(ErrorMessage = "El correo no es válido.")]
        [Display(Name = "Nuevo correo")]
        public string NewEmail { get; set; }
    }

    private async Task LoadAsync(ApplicationUser user)
    {
        var email = await userManager.GetEmailAsync(user);
        Email = email;
        Input = new InputModel { NewEmail = email };
        IsEmailConfirmed = await userManager.IsEmailConfirmedAsync(user);
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound("No se encontró el usuario.");
        }

        await LoadAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostChangeEmailAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound("No se encontró el usuario.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(user);
            return Page();
        }

        var email = await userManager.GetEmailAsync(user);
        if (Input.NewEmail == email)
        {
            StatusMessage = "Ese ya es tu correo; no hubo cambios.";
            return RedirectToPage();
        }

        var userId = await userManager.GetUserIdAsync(user);
        var code = await userManager.GenerateChangeEmailTokenAsync(user, Input.NewEmail);
        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
        var callbackUrl = Url.Page(
            "/Account/ConfirmEmailChange",
            pageHandler: null,
            values: new { area = "Identity", userId, email = Input.NewEmail, code },
            protocol: Request.Scheme);
        await emailSender.SendEmailAsync(Input.NewEmail, AccountEmails.ChangeEmailSubject, AccountEmails.ChangeEmailBody(callbackUrl));

        StatusMessage = "Te enviamos una liga al nuevo correo. Ábrela para confirmar el cambio.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSendVerificationEmailAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound("No se encontró el usuario.");
        }

        var userId = await userManager.GetUserIdAsync(user);
        var email = await userManager.GetEmailAsync(user);
        var code = await userManager.GenerateEmailConfirmationTokenAsync(user);
        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
        var callbackUrl = Url.Page(
            "/Account/ConfirmEmail",
            pageHandler: null,
            values: new { area = "Identity", userId, code },
            protocol: Request.Scheme);
        await emailSender.SendEmailAsync(email, AccountEmails.ConfirmSubject, AccountEmails.ConfirmBody(callbackUrl));

        StatusMessage = "Te enviamos el correo de confirmación.";
        return RedirectToPage();
    }
}
