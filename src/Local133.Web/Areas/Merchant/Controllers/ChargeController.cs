using System.Globalization;
using System.Security.Claims;
using Local133.Application.Auth;
using Local133.Application.Sales;
using Local133.Application.Subscriptions;
using Local133.Domain.BankAccounts;
using Local133.Domain.Sales;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Models;
using Local133.Web.Areas.Merchant.Services;
using Local133.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Local133.Web.Areas.Merchant.Controllers;

/// <summary>
/// Cobrar (/app/cobrar): venta por monto directo o con productos del catálogo (/app/cobrar/productos), con QR CoDi
/// dinámico o con notificación push al celular.
/// </summary>
[Area("Merchant")]
[Authorize(Roles = Roles.Merchant)]
[Route("app/cobrar")]
[ApiExplorerSettings(IgnoreApi = true)]
public class ChargeController(
    AppDbContext db,
    ISaleCharging charging,
    IPlanAccess plans,
    IChargeUsage usage,
    ReceiptLinks receiptLinks,
    TimeProvider clock) : Controller
{
    private const string PhoneKey = $"Input.{nameof(ChargeOptionsInput.PhoneNumber)}";

    /// <summary>Mensaje (TempData) que la vista de la venta muestra una vez.</summary>
    public const string SaleNoticeKey = "SaleNotice";

    /// <summary>Formulario vacío, con el concepto por defecto de «Mi información» si lo hay (ajustes 2026-11-26).</summary>
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await FormAsync(new ChargeInput(), cancellationToken, prefillConcept: true));

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "Input")] ChargeInput input, CancellationToken cancellationToken)
    {
        ApplyMethod(input);
        if (ModelState.IsValid)
        {
            if (!input.TryGetAmount(out var amount))
            {
                ModelState.AddModelError($"Input.{nameof(ChargeInput.Amount)}", SaleAmountMessages.Invalid);
            }
            else
            {
                var command = new CreateDirectSaleCommand(UserId, amount, input.Concept, input.Method, input.PhoneNumber);
                var result = await charging.CreateDirectSaleAsync(command, cancellationToken);
                if (Handle(result, nameof(Index), field => field switch
                    {
                        nameof(CreateDirectSaleCommand.Amount) => $"Input.{nameof(ChargeInput.Amount)}",
                        nameof(CreateDirectSaleCommand.Concept) => $"Input.{nameof(ChargeInput.Concept)}",
                        nameof(CreateDirectSaleCommand.PhoneNumber) => PhoneKey,
                        _ => string.Empty,
                    }) is { } done)
                {
                    return done;
                }
            }
        }

        var form = await FormAsync(input, cancellationToken);
        return form.Blocked is null ? View(nameof(Index), form) : RedirectToAction(nameof(Index));
    }

    /// <summary>Venta con productos: catálogo con búsqueda (misma <c>q</c>/<c>pagina</c> que la lista) y carrito.</summary>
    [HttpGet("productos")]
    public async Task<IActionResult> Cart(
        [FromQuery(Name = "q")] string? q, [FromQuery(Name = PageInfo.QueryKey)] int? page, CancellationToken cancellationToken) =>
        View(await CartFormAsync(new CartChargeInput(), q, page, [], hasErrors: false, cancellationToken));

    /// <summary>Fragmento del catálogo para la búsqueda asíncrona del carrito (<c>product-search.js</c>).</summary>
    [HttpGet("productos/catalogo")]
    public async Task<IActionResult> CartCatalog(
        [FromQuery(Name = "q")] string? q, [FromQuery(Name = PageInfo.QueryKey)] int? page, CancellationToken cancellationToken)
    {
        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        var plan = await plans.GetForMerchantAsync(merchant.Id, cancellationToken);
        if (!plan.Entitlements.CanChargeWithCart)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var model = await ProductListQuery.BuildAsync(db, merchant, q, page, Url, ChargeUrls.Cart, merchant.CanManageProducts, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return PartialView("_CartCatalogResults", model);
    }

    [HttpPost("productos")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCart([Bind(Prefix = "Input")] CartChargeInput input, CancellationToken cancellationToken)
    {
        ApplyMethod(input);

        // Los productos son campos ocultos que llena cart.js: un valor ilegible es un carrito inválido (sin campo visible).
        var lineKeys = ModelState.Keys.Where(k => k.StartsWith($"Input.{nameof(CartChargeInput.Lines)}", StringComparison.Ordinal)).ToList();
        var linesUnreadable = lineKeys.Any(k => ModelState[k]!.Errors.Count > 0);
        lineKeys.ForEach(k => ModelState.Remove(k));
        decimal expectedTotal = 0;
        if (input.Lines.Count == 0)
        {
            ModelState.AddModelError(string.Empty, SaleCartMessages.Empty);
        }
        else if (linesUnreadable)
        {
            ModelState.AddModelError(string.Empty, SaleCartMessages.QuantityInvalid);
        }
        else if (!input.TryGetExpectedTotal(out expectedTotal))
        {
            ModelState.AddModelError(string.Empty, SaleCartMessages.TotalInvalid);
        }

        if (ModelState.IsValid)
        {
            var command = new CreateCartSaleCommand(
                UserId,
                input.Lines.Select(l => new CartLine(l.ProductId, l.Quantity)).ToList(),
                expectedTotal,
                input.Concept,
                input.Method,
                input.PhoneNumber);
            var result = await charging.CreateCartSaleAsync(command, cancellationToken);
            if (Handle(result, nameof(Cart), field => field switch
                {
                    nameof(CreateCartSaleCommand.Concept) => $"Input.{nameof(CartChargeInput.Concept)}",
                    nameof(CreateCartSaleCommand.PhoneNumber) => PhoneKey,
                    _ => string.Empty, // Lines, ExpectedTotal…: al resumen general
                }) is { } done)
            {
                return done;
            }
        }

        var form = await CartFormAsync(input, null, null, await InitialLinesAsync(input.Lines, cancellationToken), hasErrors: true, cancellationToken);
        return form.Blocked is null ? View(nameof(Cart), form) : RedirectToAction(nameof(Cart));
    }

    /// <summary>QR de una venta. El filtro multi-tenant de EF Core hace que la venta de otro vendedor dé 404.</summary>
    [HttpGet("venta/{id:int}")]
    public async Task<IActionResult> Sale(int id, CancellationToken cancellationToken)
    {
        var sale = await db.Sales.AsNoTracking().Include(s => s.Items).SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (sale is null)
        {
            return NotFound();
        }

        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        var plan = await plans.GetForMerchantAsync(merchant.Id, cancellationToken);
        var now = clock.GetUtcNow();
        if (plan.HistoryStartUtc(now) is { } historyStart && sale.CreatedAt < historyStart)
        {
            // Gratuito: la venta quedó fuera de los días de historial; el detalle explica que es de Pro.
            return RedirectToAction(nameof(SalesController.Detail), "Sales", new { id });
        }

        var receipt = receiptLinks.ShareFor(
            Request, sale.ReceiptToken, merchant.BusinessName, sale.Folio, SaleQrViewModel.FormatAmount(sale.Amount),
            sale.PhoneNumber, SalesUrls.CustomerWhatsApp(sale.Id), plan.Entitlements.CanShareReceipt);
        return View(SaleQrViewModel.From(sale, now, receipt));
    }

    /// <summary>Cancela un cobro pendiente (solo local: el QR sigue vigente en CoDi hasta que expire).</summary>
    [HttpPost("venta/{id:int}/cancelar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, CancellationToken cancellationToken)
    {
        var command = new CancelSaleCommand(UserId, id);
        switch (await charging.CancelSaleAsync(command, cancellationToken))
        {
            case CancelSaleResult.NotFound:
                return NotFound();
            case CancelSaleResult.NotCancellable notCancellable:
                TempData[SaleNoticeKey] = notCancellable.Message;
                break;
            // Cancelada: la vista ya lo explica con su bloque de «cancelado».
        }

        return RedirectToAction(nameof(Sale), new { id });
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>El celular solo aplica al cobro al celular (sin JS el campo se envía aunque no aplique).</summary>
    private void ApplyMethod(ChargeOptionsInput input)
    {
        if (input.IsPushNotification)
        {
            if (string.IsNullOrWhiteSpace(input.PhoneNumber))
            {
                ModelState.AddModelError(PhoneKey, SalePhoneMessages.Required);
            }
        }
        else
        {
            ModelState.Remove(PhoneKey);
            input.PhoneNumber = null;
        }
    }

    /// <summary>
    /// Resultado del caso de uso: redirige (creada, bloqueada, push sin confirmar) o deja los errores en
    /// <see cref="ModelState"/> y devuelve <c>null</c> para volver a mostrar el formulario.
    /// </summary>
    private IActionResult? Handle(CreateSaleResult result, string formAction, Func<string, string> fieldKey)
    {
        switch (result)
        {
            case CreateSaleResult.Created created:
                return RedirectToAction(nameof(Sale), new { id = created.SaleId });

            case CreateSaleResult.NotAllowed:
            case CreateSaleResult.PlanRequired:
            case CreateSaleResult.MonthlyLimitReached:
            case CreateSaleResult.GenerationLimitReached:
                // El formulario vuelve a calcular el plan y el uso del mes y muestra el aviso correspondiente.
                return RedirectToAction(formAction);

            case CreateSaleResult.Invalid invalid:
                foreach (var (field, messages) in invalid.Errors)
                {
                    foreach (var message in messages)
                    {
                        ModelState.AddModelError(fieldKey(field), message);
                    }
                }
                return null;

            case CreateSaleResult.GatewayFailed { SaleId: { } keptSaleId }:
                // Push sin confirmar: la venta se conservó (cancelada) por si el cliente paga; la vista lo explica.
                return RedirectToAction(nameof(Sale), new { id = keptSaleId });

            case CreateSaleResult.GatewayFailed failed:
                ModelState.AddModelError(string.Empty, failed.Message);
                return null;

            default:
                throw new InvalidOperationException($"Resultado no esperado: {result}");
        }
    }

    /// <param name="prefillConcept">
    /// Solo en el <c>GET</c>: propone <c>Merchant.DefaultConcept</c>. Al volver a pintar tras un error se respeta lo enviado.
    /// </param>
    private async Task<ChargeFormViewModel> FormAsync(
        ChargeInput input, CancellationToken cancellationToken, bool prefillConcept = false)
    {
        var (merchant, account) = await MerchantAndAccountAsync(cancellationToken);
        if (prefillConcept)
        {
            input.Concept ??= merchant.DefaultConcept;
        }

        var blocked = ChargeFormViewModel.BlockedNoticeFor(merchant, account);
        var (usageModel, planBlocked) = blocked is null ? await MonthlyUsageAsync(merchant.Id, cancellationToken) : (null, null);
        string? missingCatalog = null;
        if (blocked is null && !(await plans.GetForMerchantAsync(merchant.Id, cancellationToken)).Entitlements.CanChargeWithCart)
        {
            missingCatalog = ProductListViewModel.WaitingCatalogText(await ActiveProductCountAsync(merchant.Id, cancellationToken));
        }

        return new ChargeFormViewModel
        {
            Input = input, Blocked = blocked ?? planBlocked, Usage = usageModel, MissingCatalogText = missingCatalog,
        };
    }

    /// <summary>
    /// Contador del mes y bloqueo por el límite del gratuito (5.5). Sin límite (prueba, Pro, gracia) no hay nada.
    /// </summary>
    private async Task<(ChargeUsageViewModel? Usage, ChargeBlockedNotice? Blocked)> MonthlyUsageAsync(
        int merchantId, CancellationToken cancellationToken)
    {
        var plan = await plans.GetForMerchantAsync(merchantId, cancellationToken);
        if (!plan.Entitlements.HasMonthlyChargeLimit)
        {
            return (null, null);
        }

        var month = await usage.GetMonthAsync(merchantId, cancellationToken);
        var today = Domain.Common.MexicoTime.Today(clock.GetUtcNow());
        return (ChargeUsageViewModel.From(month, today), PlanBlockedNotices.For(month, today));
    }

    private async Task<CartChargeViewModel> CartFormAsync(
        CartChargeInput input, string? q, int? page, IReadOnlyList<CartLineView> initialLines, bool hasErrors,
        CancellationToken cancellationToken)
    {
        var (merchant, account) = await MerchantAndAccountAsync(cancellationToken);
        var blocked = ChargeFormViewModel.BlockedNoticeFor(merchant, account);
        ProRequiredViewModel? proRequired = null;
        ChargeUsageViewModel? usageModel = null;
        if (blocked is null)
        {
            var plan = await plans.GetForMerchantAsync(merchant.Id, cancellationToken);
            if (!plan.Entitlements.CanChargeWithCart)
            {
                proRequired = await CartProRequiredAsync(merchant.Id, cancellationToken);
            }
            else
            {
                (usageModel, blocked) = await MonthlyUsageAsync(merchant.Id, cancellationToken);
            }
        }

        return new CartChargeViewModel
        {
            Input = input,
            Blocked = blocked,
            ProRequired = proRequired,
            Usage = usageModel,
            Catalog = blocked is null && proRequired is null
                ? await ProductListQuery.BuildAsync(db, merchant, q, page, Url, ChargeUrls.Cart, merchant.CanManageProducts, cancellationToken)
                : null,
            InitialLines = initialLines,
            HasErrors = hasErrors,
            StorageKey = $"local133:cart:{merchant.Id.ToString(CultureInfo.InvariantCulture)}",
        };
    }

    /// <summary>Productos vigentes del vendedor (para «Tu catálogo de N productos te espera»).</summary>
    private Task<int> ActiveProductCountAsync(int merchantId, CancellationToken cancellationToken) =>
        // El filtro multi-tenant ya limita al vendedor de la sesión; el MerchantId explícito es la consulta en sí.
        db.Products.AsNoTracking().CountAsync(p => p.MerchantId == merchantId && !p.IsDeleted, cancellationToken);

    /// <summary>Aviso del carrito en el gratuito, con el catálogo que ya tiene esperando.</summary>
    private async Task<ProRequiredViewModel> CartProRequiredAsync(int merchantId, CancellationToken cancellationToken)
    {
        var count = await ActiveProductCountAsync(merchantId, cancellationToken);
        return new ProRequiredViewModel(
            "Cobra con tus productos",
            "Con Pro armas la venta con tu catálogo en unos toques. Con el plan gratuito puedes cobrar por monto.",
            ProductListViewModel.WaitingCatalogText(count),
            "bi-cart3");
    }

    /// <summary>
    /// Productos enviados que siguen vigentes, con su nombre y precio actuales (en el orden en que llegaron), para que
    /// el carrito se rearme al volver con errores. Los que ya no existen se quitan.
    /// </summary>
    private async Task<IReadOnlyList<CartLineView>> InitialLinesAsync(List<CartLineInput> lines, CancellationToken cancellationToken)
    {
        var wanted = lines.Where(l => l.ProductId > 0).DistinctBy(l => l.ProductId).Take(SaleCart.MaxLines).ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        var ids = wanted.Select(l => l.ProductId).ToList();
        // El filtro multi-tenant ya limita al vendedor de la sesión.
        var products = await db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id) && !p.IsDeleted)
            .Select(p => new { p.Id, p.Name, p.Price, p.PhotoKey })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        return wanted
            .Where(l => products.ContainsKey(l.ProductId))
            .Select(l =>
            {
                var p = products[l.ProductId];
                return new CartLineView(
                    p.Id,
                    p.Name,
                    p.Price.ToString("0.00", CultureInfo.InvariantCulture),
                    SaleQrViewModel.FormatAmount(p.Price),
                    ProductListQuery.ThumbnailUrl(Url, p.Id, p.PhotoKey),
                    Math.Clamp(l.Quantity, 1, SaleItem.MaxQuantity));
            })
            .ToList();
    }

    private async Task<(Domain.Merchants.Merchant Merchant, BankAccount? Account)> MerchantAndAccountAsync(
        CancellationToken cancellationToken)
    {
        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        // El filtro multi-tenant ya limita al vendedor de la sesión; el MerchantId explícito es la consulta en sí.
        var account = await db.BankAccounts.AsNoTracking()
            .SingleOrDefaultAsync(b => b.MerchantId == merchant.Id, cancellationToken);
        return (merchant, account);
    }
}
