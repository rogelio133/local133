// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System.ComponentModel.DataAnnotations;
using Local133.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Local133.Web.Areas.Identity.Pages.Account.Manage;

public class ChangePasswordModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ILogger<ChangePasswordModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; }

    [TempData]
    public string StatusMessage { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "Escribe tu contraseña actual.")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña actual")]
        public string OldPassword { get; set; }

        [Required(ErrorMessage = "Escribe la nueva contraseña.")]
        [StringLength(100, ErrorMessage = "La contraseña debe tener entre {2} y {1} caracteres.", MinimumLength = 8)]
        [DataType(DataType.Password)]
        [Display(Name = "Nueva contraseña")]
        public string NewPassword { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Confirmar nueva contraseña")]
        [Compare("NewPassword", ErrorMessage = "Las contraseñas no coinciden.")]
        public string ConfirmPassword { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound("No se encontró el usuario.");
        }

        // Cuentas creadas con Google/Facebook no tienen contraseña: primero se define una.
        if (!await userManager.HasPasswordAsync(user))
        {
            return RedirectToPage("./SetPassword");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound("No se encontró el usuario.");
        }

        var changePasswordResult = await userManager.ChangePasswordAsync(user, Input.OldPassword, Input.NewPassword);
        if (!changePasswordResult.Succeeded)
        {
            foreach (var error in changePasswordResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return Page();
        }

        await signInManager.RefreshSignInAsync(user);
        logger.LogInformation("Usuario {UserId} cambió su contraseña.", user.Id);
        StatusMessage = "Tu contraseña se cambió.";
        return RedirectToPage();
    }
}
