// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Local133.Web.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class RegisterConfirmationModel : PageModel
{
    public string Email { get; set; }

    public IActionResult OnGet(string email, string returnUrl = null)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return LocalRedirect("~/");
        }

        // No se consulta si el usuario existe: la página es igual para cualquier correo.
        Email = email;
        return Page();
    }
}
