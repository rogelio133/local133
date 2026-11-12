using System.Security.Claims;
using Local133.Application.Auth;
using Local133.Application.BankAccounts;
using Local133.Domain.BankAccounts;
using Local133.Domain.Pey;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Areas.Merchant.Controllers;

/// <summary>Cuenta bancaria del vendedor (/app/cuenta-bancaria): alta en Pey y estatus de la revisión de Banxico.</summary>
[Area("Merchant")]
[Authorize(Roles = Roles.Merchant)]
[Route("app/cuenta-bancaria")]
[ApiExplorerSettings(IgnoreApi = true)]
public class BankAccountController(AppDbContext db, IBankAccountRegistration registration) : Controller
{
    /// <summary>Mensaje de éxito tras el alta (PRG).</summary>
    public const string SubmittedMessageKey = "BankAccountSubmitted";

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var (merchant, account) = await LoadAsync(cancellationToken);
        if (!merchant.CanSubmitBankAccount(account))
        {
            return View("Status", BankAccountStatusViewModel.From(merchant, account));
        }

        return View(FormFor(account, new BankAccountInput
        {
            // Reintento: se precargan los datos enviados (salvo la CLABE, que suele ser lo que hay que corregir).
            Entity = account?.Entity,
            HolderName = account?.HolderName,
            Alias = account?.Alias ?? merchant.BusinessName,
        }));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register([Bind(Prefix = "Input")] BankAccountInput input, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var command = new RegisterBankAccountCommand(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!,
                input.Entity!.Value,
                input.HolderName!,
                input.Clabe!,
                input.Alias!);

            switch (await registration.RegisterAsync(command, cancellationToken))
            {
                case RegisterBankAccountResult.Submitted submitted:
                    TempData[SubmittedMessageKey] = submitted.CodiStatus == PeyCodiStatus.Active
                        ? "¡Tu cuenta quedó activa! Ya puedes cobrar con CoDi."
                        : "Recibimos tu cuenta. Banxico la revisará; puede tardar hasta 5 días hábiles.";
                    return RedirectToAction(nameof(Index));

                case RegisterBankAccountResult.NotAllowed:
                    return RedirectToAction(nameof(Index));

                case RegisterBankAccountResult.Invalid invalid:
                    foreach (var (field, messages) in invalid.Errors)
                    {
                        var key = string.IsNullOrEmpty(field) ? string.Empty : $"Input.{field}";
                        foreach (var message in messages)
                        {
                            ModelState.AddModelError(key, message);
                        }
                    }
                    break;

                case RegisterBankAccountResult.GatewayFailed failed:
                    ModelState.AddModelError(string.Empty, failed.Message);
                    break;
            }
        }

        var (merchant, account) = await LoadAsync(cancellationToken);
        if (!merchant.CanSubmitBankAccount(account))
        {
            return RedirectToAction(nameof(Index));
        }

        return View(nameof(Index), FormFor(account, input));
    }

    private async Task<(Domain.Merchants.Merchant Merchant, BankAccount? Account)> LoadAsync(CancellationToken cancellationToken)
    {
        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        // El filtro multi-tenant de EF Core ya limita al vendedor de la sesión; el MerchantId explícito es la consulta en sí.
        var account = await db.BankAccounts.AsNoTracking().SingleOrDefaultAsync(b => b.MerchantId == merchant.Id, cancellationToken);
        return (merchant, account);
    }

    private static BankAccountFormViewModel FormFor(BankAccount? previous, BankAccountInput input) => new()
    {
        Input = input,
        PreviousError = previous?.CodiStatus == PeyCodiStatus.Error ? previous.ErrorMessage : null,
        PreviousMaskedClabe = previous is null ? null : Clabe.Mask(previous.Clabe),
    };
}
