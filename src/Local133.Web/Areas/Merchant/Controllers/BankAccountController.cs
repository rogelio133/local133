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
public class BankAccountController(
    AppDbContext db, IBankAccountRegistration registration, IBankAccountStatusSync statusSync) : Controller
{
    /// <summary>Mensaje de éxito tras el alta o la verificación (PRG).</summary>
    public const string SubmittedMessageKey = "BankAccountSubmitted";

    /// <summary>Aviso informativo tras verificar el estatus (sigue en revisión, consulta muy seguida).</summary>
    public const string StatusInfoKey = "BankAccountStatusInfo";

    /// <summary>Aviso de falla al verificar el estatus.</summary>
    public const string StatusWarningKey = "BankAccountStatusWarning";

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

    /// <summary>Botón "Verificar estatus": consulta a Pey la cuenta en revisión del vendedor de la sesión.</summary>
    [HttpPost("verificar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckStatus(CancellationToken cancellationToken)
    {
        // La cuenta sale de la sesión (filtro multi-tenant), nunca de un id enviado por el cliente.
        var (merchant, account) = await LoadAsync(cancellationToken);
        if (account is null || !BankAccountStatusViewModel.CanCheck(merchant, account))
        {
            return RedirectToAction(nameof(Index));
        }

        switch (await statusSync.SyncAsync(account.Id, BankAccountStatusChecks.ManualMinInterval, cancellationToken))
        {
            case BankAccountStatusSyncResult.Synced { Current: PeyCodiStatus.Active }:
                TempData[SubmittedMessageKey] = "¡Tu cuenta ya está activa! Ya puedes comenzar a vender con CoDi.";
                break;

            case BankAccountStatusSyncResult.Synced { Current: PeyCodiStatus.Error }:
                // Index muestra el formulario de reintento con el motivo del rechazo.
                break;

            case BankAccountStatusSyncResult.Synced:
                TempData[StatusInfoKey] = "Banxico sigue revisando tu cuenta. Te avisaremos por correo en cuanto quede activa.";
                break;

            case BankAccountStatusSyncResult.TooSoon tooSoon:
                var seconds = Math.Max(1, (int)Math.Ceiling(tooSoon.RetryAfter.TotalSeconds));
                TempData[StatusInfoKey] = $"Acabamos de revisar tu cuenta. Intenta de nuevo en {seconds} segundos.";
                break;

            case BankAccountStatusSyncResult.GatewayFailed failed:
                TempData[StatusWarningKey] = failed.Message;
                break;
        }

        return RedirectToAction(nameof(Index));
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
