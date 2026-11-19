using Local133.Application.Auth;
using Local133.Application.Payments;
using Local133.Application.Sales;
using Local133.Domain.Sales;
using Local133.Infrastructure.Persistence;
using Local133.Web.Areas.Merchant.Models;
using Local133.Web.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Local133.Web.Areas.Merchant.Controllers;

/// <summary>
/// <b>Prueba de concepto</b> del link de pago de Pey (/app/link-de-pago): crea el link con monto y concepto y manda al
/// vendedor directo a la página de pago de Pey, para ver cómo se ve. Solo existe en Development: Pey no acepta
/// <c>bankAccountId</c> en los links, así que el dinero caería en la cuenta de la plataforma (choca con la regla de que
/// la plataforma nunca toca el dinero del vendedor). No guarda nada en la base. Lo reutilizable es
/// <see cref="IPaymentGateway.CreatePaymentLinkAsync"/> / <see cref="IPaymentGateway.GetPaymentLinkAsync"/>.
/// </summary>
[Area("Merchant")]
[Authorize(Roles = Roles.Merchant)]
[Route("app/link-de-pago")]
[ApiExplorerSettings(IgnoreApi = true)]
public class PaymentLinkPocController(
    AppDbContext db,
    IPaymentGateway gateway,
    IOptions<PublicSiteOptions> site,
    IWebHostEnvironment environment,
    ILogger<PaymentLinkPocController> logger) : Controller
{
    private const string LinkIdKey = "PaymentLinkPocId";
    private const string ReturnPath = "/app/link-de-pago/regreso";

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!environment.IsDevelopment())
        {
            context.Result = NotFound();
        }
    }

    [HttpGet("")]
    public IActionResult Index() => View(Form(new PaymentLinkPocInput()));

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "Input")] PaymentLinkPocInput input, CancellationToken cancellationToken)
    {
        var baseUrl = site.Value.BaseUrlFor(Request);
        if (!ChargeInput.TryParseAmount(input.Amount, out var amount) || !Sale.IsValidAmount(amount))
        {
            ModelState.AddModelError($"Input.{nameof(PaymentLinkPocInput.Amount)}", SaleAmountMessages.Invalid);
        }

        if (HttpsWarningFor(baseUrl) is { } warning)
        {
            ModelState.AddModelError(string.Empty, warning);
        }

        if (!ModelState.IsValid)
        {
            return View(nameof(Index), Form(input));
        }

        var merchant = await db.GetCurrentMerchantAsync(User, cancellationToken);
        var concept = input.Concept!.Trim();
        var request = new CreatePaymentLinkRequest(
            amount,
            SuccessUrl: new Uri($"{baseUrl}{ReturnPath}?resultado=pagado"),
            BackUrl: new Uri($"{baseUrl}{ReturnPath}?resultado=cancelado"),
            ClientReferenceId: concept,
            Metadata: new Dictionary<string, string>
            {
                ["kind"] = "payment-link-poc",
                ["merchantId"] = merchant.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["concept"] = concept,
            });

        try
        {
            var link = await gateway.CreatePaymentLinkAsync(request, cancellationToken);
            logger.LogInformation("PoC: link de pago {PaymentLinkId} creado para el vendedor {MerchantId}", link.Id, merchant.Id);
            TempData[LinkIdKey] = link.Id;
            return Redirect(link.Url.AbsoluteUri);
        }
        catch (PaymentGatewayValidationException ex)
        {
            ModelState.AddModelError(string.Empty, $"Pey rechazó el link: {ex.GatewayMessage ?? ex.Message}");
        }
        catch (PaymentGatewayUnavailableException)
        {
            ModelState.AddModelError(string.Empty, "Pey no respondió. El link pudo haberse creado; revisa el portal de Pey antes de reintentar.");
        }
        catch (PaymentGatewayException ex)
        {
            ModelState.AddModelError(string.Empty, $"Pey respondió con error: {ex.GatewayMessage ?? ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message); // falta Pey:ApiKey
        }

        return View(nameof(Index), Form(input));
    }

    /// <summary><c>successUrl</c> y <c>backUrl</c> del link: muestra lo que responde <c>GET /payment-link/{id}</c>.</summary>
    [HttpGet("regreso")]
    public async Task<IActionResult> Return([FromQuery(Name = "resultado")] string? result, CancellationToken cancellationToken)
    {
        // Peek: si el vendedor recarga la página, se vuelve a consultar el mismo link.
        var linkId = TempData.Peek(LinkIdKey) as string;
        GatewayPaymentLink? link = null;
        string? error = null;
        if (linkId is not null)
        {
            try
            {
                link = await gateway.GetPaymentLinkAsync(linkId, cancellationToken);
            }
            catch (PaymentGatewayException ex)
            {
                error = ex.GatewayMessage ?? ex.Message;
            }
            catch (InvalidOperationException ex)
            {
                error = ex.Message;
            }
        }

        return View(new PaymentLinkPocReturnViewModel(result, linkId, link, error));
    }

    private PaymentLinkPocFormViewModel Form(PaymentLinkPocInput input) =>
        new(input, HttpsWarningFor(site.Value.BaseUrlFor(Request)));

    private static string? HttpsWarningFor(string baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? null
            : "Pey exige HTTPS para las URLs de regreso: arranca con el perfil https (https://localhost:7180) o configura App:PublicBaseUrl con la URL del túnel.";
}
