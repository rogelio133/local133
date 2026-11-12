using System.Security.Claims;
using Local133.Application.Auth;
using Local133.Application.Sales;
using Local133.Domain.BankAccounts;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Areas.Merchant.Controllers;

/// <summary>Cobrar (/app/cobrar): venta por monto directo con QR CoDi dinámico.</summary>
[Area("Merchant")]
[Authorize(Roles = Roles.Merchant)]
[Route("app/cobrar")]
[ApiExplorerSettings(IgnoreApi = true)]
public class ChargeController(AppDbContext db, ISaleCharging charging, TimeProvider clock) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await FormAsync(new ChargeInput(), cancellationToken));

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "Input")] ChargeInput input, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            if (!input.TryGetAmount(out var amount))
            {
                ModelState.AddModelError($"Input.{nameof(ChargeInput.Amount)}", SaleAmountMessages.Invalid);
            }
            else
            {
                var command = new CreateDirectSaleCommand(User.FindFirstValue(ClaimTypes.NameIdentifier)!, amount, input.Concept);
                switch (await charging.CreateDirectSaleAsync(command, cancellationToken))
                {
                    case CreateDirectSaleResult.Created created:
                        return RedirectToAction(nameof(Sale), new { id = created.SaleId });

                    case CreateDirectSaleResult.NotAllowed:
                        return RedirectToAction(nameof(Index));

                    case CreateDirectSaleResult.Invalid invalid:
                        foreach (var (field, messages) in invalid.Errors)
                        {
                            // Amount y Concept vienen del formulario; UserId no.
                            var key = field switch
                            {
                                nameof(CreateDirectSaleCommand.Amount) => $"Input.{nameof(ChargeInput.Amount)}",
                                nameof(CreateDirectSaleCommand.Concept) => $"Input.{nameof(ChargeInput.Concept)}",
                                _ => string.Empty,
                            };
                            foreach (var message in messages)
                            {
                                ModelState.AddModelError(key, message);
                            }
                        }
                        break;

                    case CreateDirectSaleResult.GatewayFailed failed:
                        ModelState.AddModelError(string.Empty, failed.Message);
                        break;
                }
            }
        }

        var form = await FormAsync(input, cancellationToken);
        return form.Blocked is null ? View(nameof(Index), form) : RedirectToAction(nameof(Index));
    }

    /// <summary>QR de una venta. El filtro multi-tenant de EF Core hace que la venta de otro vendedor dé 404.</summary>
    [HttpGet("venta/{id:int}")]
    public async Task<IActionResult> Sale(int id, CancellationToken cancellationToken)
    {
        var sale = await db.Sales.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (sale is null)
        {
            return NotFound();
        }

        return View(SaleQrViewModel.From(sale, clock.GetUtcNow()));
    }

    private async Task<ChargeFormViewModel> FormAsync(ChargeInput input, CancellationToken cancellationToken)
    {
        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        // El filtro multi-tenant ya limita al vendedor de la sesión; el MerchantId explícito es la consulta en sí.
        BankAccount? account = await db.BankAccounts.AsNoTracking()
            .SingleOrDefaultAsync(b => b.MerchantId == merchant.Id, cancellationToken);
        return new ChargeFormViewModel { Input = input, Blocked = ChargeFormViewModel.BlockedNoticeFor(merchant, account) };
    }
}
