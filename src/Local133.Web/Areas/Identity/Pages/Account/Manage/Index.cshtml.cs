// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System.ComponentModel.DataAnnotations;
using Local133.Application.Auth;
using Local133.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Local133.Web.Areas.Identity.Pages.Account.Manage;

/// <summary>
/// Perfil de la cuenta: solo el correo. Desde la 4.1 el celular (<c>AspNetUsers.PhoneNumber</c>) se edita en «Mi
/// información» (<c>/app/mi-informacion</c>), junto con los datos del negocio.
/// </summary>
public class IndexModel(UserManager<ApplicationUser> userManager) : PageModel
{
    [Display(Name = "Correo")]
    public string Username { get; set; }

    /// <summary>Vendedor: se le muestra la liga a «Mi información».</summary>
    public bool IsMerchant { get; set; }

    [TempData]
    public string StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound("No se encontró el usuario.");
        }

        Username = await userManager.GetUserNameAsync(user);
        IsMerchant = User.IsInRole(Roles.Merchant);
        return Page();
    }
}
