// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System.ComponentModel.DataAnnotations;
using Local133.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Local133.Web.Areas.Identity.Pages.Account.Manage;

public class IndexModel(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager) : PageModel
{
    [Display(Name = "Correo")]
    public string Username { get; set; }

    [TempData]
    public string StatusMessage { get; set; }

    [BindProperty]
    public InputModel Input { get; set; }

    public class InputModel
    {
        [RegularExpression(@"^\d{10}$", ErrorMessage = "Escribe 10 dígitos, sin espacios ni lada internacional.")]
        [Display(Name = "Celular (10 dígitos)")]
        public string PhoneNumber { get; set; }
    }

    private async Task LoadAsync(ApplicationUser user)
    {
        Username = await userManager.GetUserNameAsync(user);
        Input = new InputModel { PhoneNumber = await userManager.GetPhoneNumberAsync(user) };
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

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound("No se encontró el usuario.");
        }

        if (!ModelState.IsValid)
        {
            Username = await userManager.GetUserNameAsync(user);
            return Page();
        }

        var phoneNumber = await userManager.GetPhoneNumberAsync(user);
        if (Input.PhoneNumber != phoneNumber)
        {
            var setPhoneResult = await userManager.SetPhoneNumberAsync(user, Input.PhoneNumber);
            if (!setPhoneResult.Succeeded)
            {
                StatusMessage = "Error: no se pudo guardar el celular.";
                return RedirectToPage();
            }
        }

        await signInManager.RefreshSignInAsync(user);
        StatusMessage = "Tu perfil se actualizó.";
        return RedirectToPage();
    }
}
