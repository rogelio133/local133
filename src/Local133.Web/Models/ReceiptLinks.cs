using Local133.Domain.Sales;
using Local133.Web.Hosting;
using Microsoft.Extensions.Options;

namespace Local133.Web.Models;

/// <summary>
/// Ligas del recibo público de una venta (<c>/recibo/{token}</c>): la URL absoluta y el mensaje para WhatsApp.
/// </summary>
public sealed class ReceiptLinks(IOptions<PublicSiteOptions> options)
{
    public const string PathPrefix = "/recibo/";

    /// <summary>Ruta del recibo (relativa a la raíz del sitio).</summary>
    public static string PathFor(string token) => PathPrefix + token;

    /// <summary>URL absoluta del recibo (base de <see cref="PublicSiteOptions.BaseUrlFor"/>).</summary>
    public string UrlFor(HttpRequest request, string token) => options.Value.BaseUrlFor(request) + PathFor(token);

    /// <summary>
    /// Todo lo necesario para compartir el recibo. En un cobro por celular además se ofrece mandarlo al chat de ese
    /// número (el cliente que pagó) por <paramref name="customerWhatsAppPath"/>: un endpoint del sitio que redirige a
    /// WhatsApp, para que el celular completo nunca quede escrito en la página (solo se muestra enmascarado).
    /// </summary>
    /// <param name="canShare">
    /// El plan permite los botones de WhatsApp y «Compartir» (Pro). En el gratuito solo se copia la liga (5.5).
    /// </param>
    public ReceiptShare ShareFor(
        HttpRequest request, string token, string? businessName, int folio, string amountText, string? customerPhone = null,
        string? customerWhatsAppPath = null, bool canShare = true)
    {
        var url = UrlFor(request, token);
        var message = Message(businessName, folio, amountText, url);
        var toCustomer = customerWhatsAppPath is not null && customerPhone is not null && PhoneNumber.IsValid(customerPhone);
        return new ReceiptShare(
            url,
            PathFor(token),
            message,
            WhatsAppUrl(message),
            WhatsAppAppUrl(message),
            WhatsAppWebUrl(message),
            toCustomer ? customerWhatsAppPath : null,
            toCustomer ? PhoneNumber.Mask(customerPhone!) : null,
            canShare);
    }

    /// <summary>Aviso cuando el gratuito intenta mandar el recibo por WhatsApp.</summary>
    public const string ShareRequiresPro =
        "Con el plan gratuito copia la liga del recibo y pégala donde quieras. Con Pro lo envías por WhatsApp en un toque.";

    /// <summary>
    /// Liga <c>wa.me</c> con el mensaje: sin número abre el selector de contactos; con un celular mexicano de 10
    /// dígitos abre el chat con él. En el celular abre la app; en escritorio abre una página intermedia de WhatsApp que
    /// no siempre detecta la app instalada (por eso en escritorio se usa <see cref="WhatsAppAppUrl"/>).
    /// </summary>
    public static string WhatsAppUrl(string message, string? phone = null) =>
        IsValidPhone(phone)
            ? $"https://wa.me/52{phone}?text={Uri.EscapeDataString(message)}"
            : $"https://wa.me/?text={Uri.EscapeDataString(message)}";

    /// <summary>
    /// Abre la app de escritorio de WhatsApp (protocolo <c>whatsapp:</c>, lo registra la app al instalarse). Si no está
    /// instalada no pasa nada: por eso junto a esta liga se ofrece <see cref="WhatsAppWebUrl"/>.
    /// </summary>
    public static string WhatsAppAppUrl(string message, string? phone = null) =>
        IsValidPhone(phone)
            ? $"whatsapp://send?phone=52{phone}&text={Uri.EscapeDataString(message)}"
            : $"whatsapp://send?text={Uri.EscapeDataString(message)}";

    /// <summary>WhatsApp Web en el navegador (respaldo en escritorio sin la app).</summary>
    public static string WhatsAppWebUrl(string message, string? phone = null) =>
        IsValidPhone(phone)
            ? $"https://web.whatsapp.com/send?phone=52{phone}&text={Uri.EscapeDataString(message)}"
            : $"https://web.whatsapp.com/send?text={Uri.EscapeDataString(message)}";

    /// <summary>Destino de WhatsApp según el dispositivo: <c>app</c> (escritorio), <c>web</c> o, por omisión, <c>wa.me</c>.</summary>
    public static string WhatsAppUrlFor(string? destination, string message, string? phone = null) => destination switch
    {
        "app" => WhatsAppAppUrl(message, phone),
        "web" => WhatsAppWebUrl(message, phone),
        _ => WhatsAppUrl(message, phone),
    };

    private static bool IsValidPhone(string? phone) => phone is not null && PhoneNumber.IsValid(phone);

    /// <summary>«Recibo de pago de Tacos Don Ramón · Venta 12 por $150.00» + la liga, en tres renglones.</summary>
    public static string Message(string? businessName, int folio, string amountText, string url)
    {
        var title = string.IsNullOrWhiteSpace(businessName) ? "Recibo de pago" : $"Recibo de pago de {businessName.Trim()}";
        return $"{title}\nVenta {folio} por {amountText}\n{url}";
    }
}

/// <param name="Url">URL absoluta del recibo.</param>
/// <param name="Path">Ruta del recibo (para enlazarlo dentro del sitio).</param>
/// <param name="Message">Texto que se comparte (incluye la URL).</param>
/// <param name="WhatsAppUrl">Liga <c>wa.me</c> con el mensaje (el vendedor elige el contacto); para el celular.</param>
/// <param name="WhatsAppAppUrl">Liga <c>whatsapp://send</c> que abre la app de escritorio.</param>
/// <param name="WhatsAppWebUrl">WhatsApp Web (respaldo en escritorio sin la app).</param>
/// <param name="CustomerWhatsAppPath">
/// Ruta del sitio que redirige al chat del cliente (cobro por celular); acepta <c>?destino=app|web</c>.
/// </param>
/// <param name="CustomerMaskedPhone">Celular (enmascarado) del cliente, si hay <paramref name="CustomerWhatsAppPath"/>.</param>
/// <param name="CanShare">Botones de WhatsApp y «Compartir» (Pro); sin ellos solo se copia la liga (gratuito).</param>
public sealed record ReceiptShare(
    string Url, string Path, string Message, string WhatsAppUrl, string WhatsAppAppUrl, string WhatsAppWebUrl,
    string? CustomerWhatsAppPath, string? CustomerMaskedPhone, bool CanShare = true);
