using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Local133.Application.Sales;
using Local133.Domain.BankAccounts;
using Local133.Domain.Common;
using Local133.Domain.Merchants;
using Local133.Domain.Pey;
using Local133.Domain.Sales;
using Local133.Web.Validation;

namespace Local133.Web.Areas.Merchant.Models;

/// <summary>
/// Monto del cobro. Llega como texto y se convierte con cultura invariante (punto decimal): así no depende de la
/// cultura del servidor y no se aceptan signos, exponentes ni separadores de miles.
/// </summary>
public sealed class ChargeInput : ChargeOptionsInput
{
    /// <summary>Hasta 6 enteros y 2 decimales, sin signo. La misma regla aplica <c>amount-input.js</c> al teclear.</summary>
    public const string AmountPattern = @"^\d{1,6}(\.\d{1,2})?$";

    /// <summary>Valor inicial del campo: un centavo (lo mínimo que acepta CoDi, útil para probar).</summary>
    public const string DefaultAmount = "0.01";

    [Required(ErrorMessage = SaleAmountMessages.Required)]
    [RegularExpression(AmountPattern, ErrorMessage = SaleAmountMessages.Invalid)]
    [Display(Name = "Monto a cobrar")]
    public string? Amount { get; set; } = DefaultAmount;

    public bool TryGetAmount(out decimal amount) => TryParseAmount(Amount, out amount);

    /// <summary>Monto en texto (<see cref="AmountPattern"/>) con cultura invariante.</summary>
    public static bool TryParseAmount(string? text, out decimal amount) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);
}

/// <summary>
/// Venta con carrito (/app/cobrar/productos). <c>cart.js</c> llena los productos (campos ocultos) y el total que vio
/// el vendedor; el servidor vuelve a tomar los precios del catálogo y no cobra si el total cambió.
/// </summary>
public sealed class CartChargeInput : ChargeOptionsInput
{
    public List<CartLineInput> Lines { get; set; } = [];

    /// <summary>
    /// Total que vio el vendedor (texto invariante con 2 decimales). Sin atributos: lo revisa el controlador y el
    /// error va al resumen general (no tiene campo visible).
    /// </summary>
    public string? ExpectedTotal { get; set; }

    public bool TryGetExpectedTotal(out decimal total)
    {
        total = 0;
        return ExpectedTotal is not null
            && System.Text.RegularExpressions.Regex.IsMatch(ExpectedTotal, ChargeInput.AmountPattern)
            && ChargeInput.TryParseAmount(ExpectedTotal, out total);
    }
}

public sealed class CartLineInput
{
    public int ProductId { get; set; }

    public int Quantity { get; set; }
}

/// <summary>Concepto, método (QR o celular) y celular: comunes al cobro por monto y al de carrito.</summary>
public abstract class ChargeOptionsInput
{
    /// <summary>Concepto del cobro; es lo que ve el pagador en su app bancaria (sin el nombre del negocio).</summary>
    [Required(ErrorMessage = SaleConceptMessages.Required)]
    [StringLength(Sale.ConceptTextMaxLength, ErrorMessage = SaleConceptMessages.TooLong)]
    [RegularExpression(Sale.ConceptTextPattern, ErrorMessage = SaleConceptMessages.Invalid)]
    [Display(Name = "Concepto")]
    public string? Concept { get; set; }

    /// <summary>Cómo se cobra: QR (por defecto) o notificación push al celular del cliente.</summary>
    public ChargeMethod Method { get; set; } = ChargeMethod.DynamicQr;

    /// <summary>
    /// Celular del cliente; obligatorio solo al cobrar al celular (lo revisa <see cref="Controllers.ChargeController"/>;
    /// en cliente el campo oculto queda deshabilitado y jQuery Validation lo ignora).
    /// </summary>
    [MexicanPhone]
    [Display(Name = "Celular de tu cliente")]
    public string? PhoneNumber { get; set; }

    public bool IsPushNotification => Method == ChargeMethod.PushNotification;

    /// <summary>Largo máximo del campo de celular: 10 dígitos más espacios, guiones o la lada (<c>+52 (55) 1234-5678</c>).</summary>
    public const int PhoneNumberInputMaxLength = 20;
}

/// <summary>
/// Pantalla "Cobrar con productos": catálogo con búsqueda y carrito, o aviso si el vendedor aún no puede cobrar.
/// </summary>
public sealed class CartChargeViewModel
{
    public CartChargeInput Input { get; set; } = new();

    public ChargeBlockedNotice? Blocked { get; init; }

    /// <summary>Plan vencido (5.14): el panel muestra el aviso en lugar del catálogo.</summary>
    public PlanExpiredViewModel? Expired { get; init; }

    /// <summary>Básico: el carrito es de Pro y el panel muestra este aviso en lugar del catálogo.</summary>
    public ProRequiredViewModel? ProRequired { get; init; }

    /// <summary>Primera página del catálogo (con la búsqueda de la URL); <c>null</c> si no puede cobrar.</summary>
    public ProductListViewModel? Catalog { get; init; }

    /// <summary>
    /// Productos enviados que siguen en el catálogo, con su precio vigente, para que <c>cart.js</c> rearme el carrito
    /// cuando el formulario vuelve con errores. Vacío en un GET (el carrito sale de <c>sessionStorage</c>).
    /// </summary>
    public IReadOnlyList<CartLineView> InitialLines { get; init; } = [];

    /// <summary>El formulario vuelve con errores del servidor (en móvil se abre el panel del carrito).</summary>
    public bool HasErrors { get; init; }

    /// <summary>Llave del carrito en <c>sessionStorage</c> (por vendedor).</summary>
    public string StorageKey { get; init; } = "local133:cart";
}

/// <summary>Producto del carrito tal como lo usa <c>cart.js</c> (precio en texto invariante con 2 decimales).</summary>
public sealed record CartLineView(int ProductId, string Name, string Price, string PriceText, string? Thumb, int Quantity);

/// <summary>Producto de una venta con carrito (copia guardada al cobrar).</summary>
public sealed record SaleItemView(string Name, int Quantity, string UnitPriceText, string AmountText);

/// <summary>Pantalla "Cobrar": formulario de monto, o aviso si el vendedor aún no puede cobrar.</summary>
public sealed class ChargeFormViewModel
{
    public ChargeInput Input { get; set; } = new();

    /// <summary>Aviso cuando no puede cobrar; <c>null</c> = puede cobrar.</summary>
    public ChargeBlockedNotice? Blocked { get; init; }

    /// <summary>Plan vencido (5.14): en lugar del formulario va el aviso con «Elegir plan».</summary>
    public PlanExpiredViewModel? Expired { get; init; }

    /// <summary>El formulario se muestra: puede cobrar por estatus y por plan.</summary>
    public bool ShowsForm => Blocked is null && Expired is null;

    /// <summary>
    /// Lo que te pierdes (5.10): «Tu catálogo de 63 productos te espera en Pro.» en Básico con productos; <c>null</c>
    /// si puede cobrar con carrito, si venció o si no tiene productos.
    /// </summary>
    public string? MissingCatalogText { get; init; }

    public static ChargeBlockedNotice? BlockedNoticeFor(Domain.Merchants.Merchant merchant, BankAccount? account)
    {
        if (merchant.CanCharge(account))
        {
            return null;
        }

        return merchant.Status switch
        {
            MerchantStatus.PendingBankAccount when account?.CodiStatus == PeyCodiStatus.Error => new(
                "Corrige tu cuenta bancaria",
                "Banxico rechazó tu cuenta. Corrígela para empezar a cobrar con CoDi.",
                "~/app/cuenta-bancaria", "Corregir mi cuenta"),
            MerchantStatus.PendingBankAccount => new(
                "Registra tu cuenta bancaria",
                "Para cobrar con CoDi necesitas registrar la CLABE donde recibirás tu dinero.",
                "~/app/cuenta-bancaria", "Registrar mi CLABE"),
            MerchantStatus.BankAccountInReview => new(
                "Tu cuenta está en revisión",
                "Banxico está revisando tu cuenta bancaria; puede tardar hasta 5 días hábiles. Podrás cobrar en cuanto quede activa.",
                "~/app/cuenta-bancaria", "Ver estatus"),
            MerchantStatus.Active => new(
                "Tu cuenta bancaria no está activa",
                "Tu cuenta bancaria no está activa para CoDi. Revísala para volver a cobrar.",
                "~/app/cuenta-bancaria", "Ver cuenta bancaria"),
            MerchantStatus.Suspended => new(
                "Tu cuenta está suspendida",
                "Por ahora no puedes generar cobros. Ponte en contacto con nosotros.",
                "~/app", "Volver al inicio"),
            _ => new(
                "Tu cuenta está dada de baja",
                "No puedes generar cobros. Ponte en contacto con nosotros.",
                "~/app", "Volver al inicio"),
        };
    }
}

public sealed record ChargeBlockedNotice(string Title, string Message, string LinkHref, string LinkText);

/// <summary>Rutas del cobro (relativas a la raíz del sitio, para <c>Url.Content("~" + …)</c>).</summary>
public static class ChargeUrls
{
    /// <summary>Cobro por monto.</summary>
    public const string Amount = "/app/cobrar";

    /// <summary>Cobro con productos del catálogo.</summary>
    public const string Cart = "/app/cobrar/productos";

    /// <summary>Fragmento del catálogo para la búsqueda asíncrona del carrito.</summary>
    public const string CartCatalog = "/app/cobrar/productos/catalogo";
}

/// <summary>
/// Pantalla de una venta: pendiente (QR, o aviso del cobro enviado al celular, + cuenta regresiva + cancelar), pagada
/// («Pago recibido»), vencida o cancelada. El servidor la dibuja en su estado actual y <c>sale-qr.js</c> pasa a
/// «Pago recibido» o «expirado» sin recargar.
/// </summary>
public sealed record SaleQrViewModel(
    int Id,
    int Folio,
    string AmountText,
    string? Concept,
    SaleStatus Status,
    ChargeMethod Method,
    string? QrString,
    string? MaskedPhone,
    bool HasPaymentRequest,
    int? RemainingSeconds,
    string? PaidAtText,
    IReadOnlyList<SaleItemView> Items,
    Web.Models.ReceiptShare Receipt)
{
    /// <summary>Detalle de la venta en el historial.</summary>
    public string DetailHref => SalesUrls.Detail(Id);

    /// <summary>Venta con carrito: muestra sus productos y «Nuevo cobro» vuelve al catálogo.</summary>
    public bool IsCart => Items.Count > 0;

    public string NewChargeHref => IsCart ? ChargeUrls.Cart : ChargeUrls.Amount;

    /// <summary>«1 producto» / «3 productos» (suma de cantidades).</summary>
    public string ItemsSummary
    {
        get
        {
            var count = Items.Sum(i => i.Quantity);
            return count == 1 ? "1 producto" : $"{count:N0} productos";
        }
    }

    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-MX");

    /// <summary>Cobro por notificación push al celular (sin QR).</summary>
    public bool IsPushNotification => Method == ChargeMethod.PushNotification;

    /// <summary>Sigue esperando el pago: pendiente, con su QR (o su push enviado) y sin vencer.</summary>
    public bool IsAwaitingPayment => Status == SaleStatus.Pending && HasPaymentRequest && RemainingSeconds is not 0;

    /// <summary>Se puede mostrar el QR para pagar.</summary>
    public bool ShowQr => IsAwaitingPayment && !IsPushNotification && QrString is not null;

    /// <summary>Se muestra el aviso del cobro enviado al celular.</summary>
    public bool ShowPushRequest => IsAwaitingPayment && IsPushNotification;

    /// <summary>Venció por tiempo (o nunca tuvo QR / push) sin pagarse.</summary>
    public bool IsExpired => Status == SaleStatus.Expired || (Status == SaleStatus.Pending && !IsAwaitingPayment);

    /// <summary>Cómo se llama lo que el cliente paga, para los textos: «QR» o «solicitud».</summary>
    public string RequestNoun => IsPushNotification ? "solicitud" : "QR";

    public bool IsPaid => Status == SaleStatus.Paid;

    public bool IsCancelled => Status == SaleStatus.Cancelled;

    /// <summary>La vista escucha el pago (SignalR + polling): pendiente, o vencida/cancelada por si llega un pago tardío.</summary>
    public bool ListensForPayment => !IsPaid;

    /// <summary>El vendedor puede cancelar: el QR (o la solicitud al celular) sigue vigente.</summary>
    public bool CanCancel => IsAwaitingPayment;

    public string? RemainingText => RemainingSeconds is { } s ? FormatCountdown(s) : null;

    /// <param name="receipt">
    /// Recibo para compartir. Se dibuja oculto aunque la venta siga pendiente: <c>sale-qr.js</c> lo muestra al llegar
    /// el pago (la liga solo responde cuando la venta ya está pagada).
    /// </param>
    public static SaleQrViewModel From(Sale sale, DateTimeOffset now, Web.Models.ReceiptShare receipt) => new(
        sale.Id,
        sale.Folio,
        FormatAmount(sale.Amount),
        sale.Concept,
        sale.Status,
        sale.Method,
        sale.QrString,
        sale.PhoneNumber is null ? null : Domain.Sales.PhoneNumber.Mask(sale.PhoneNumber),
        sale.HasPaymentRequest,
        sale.RemainingSeconds(now),
        sale.PaidAt is { } paidAt ? FormatPaidAt(paidAt) : null,
        sale.Items
            .OrderBy(i => i.Id)
            .Select(i => new SaleItemView(i.Name, i.Quantity, FormatAmount(i.UnitPrice), FormatAmount(i.Amount)))
            .ToList(),
        receipt);

    public static string FormatAmount(decimal amount) => amount.ToString("C2", Spanish);

    /// <summary>Hora del pago en la hora del centro de México, p. ej. «12 de noviembre, 3:45 p. m.».</summary>
    public static string FormatPaidAt(DateTimeOffset paidAt) =>
        MexicoTime.ToLocal(paidAt).ToString("d 'de' MMMM, h:mm tt", Spanish);

    /// <summary><c>mm:ss</c>, igual que la cuenta regresiva de <c>sale-qr.js</c>.</summary>
    public static string FormatCountdown(int seconds) =>
        $"{seconds / 60:D2}:{seconds % 60:D2}";
}
