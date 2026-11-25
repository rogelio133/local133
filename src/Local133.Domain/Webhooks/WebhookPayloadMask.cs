using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Local133.Domain.BankAccounts;
using Local133.Domain.Sales;

namespace Local133.Domain.Webhooks;

/// <summary>
/// Payload de un webhook listo para mostrarlo en el admin (6.4): JSON con sangría y sin datos sensibles. La CLABE (del
/// pagador o del vendedor), el RFC y el celular se enmascaran por llave, y cualquier serie de 16 dígitos o más (CLABE o
/// tarjeta) se enmascara donde esté. El body crudo nunca se muestra.
/// </summary>
public static partial class WebhookPayloadMask
{
    /// <summary>Series de dígitos que se enmascaran en cualquier valor del JSON.</summary>
    public const int JsonDigitRunLength = 16;

    /// <summary>Series de dígitos que se enmascaran si el payload no es JSON (más estricto: no se sabe qué es cada cosa).</summary>
    public const int TextDigitRunLength = 10;

    private static readonly HashSet<string> ClabeKeys = new(StringComparer.OrdinalIgnoreCase) { "clabe" };

    private static readonly HashSet<string> TaxIdKeys = new(StringComparer.OrdinalIgnoreCase) { "taxId", "rfc", "curp" };

    private static readonly HashSet<string> PhoneKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "phone", "phoneNumber", "cellphone", "cellPhone", "mobile",
    };

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        // Solo se muestra en una página (Razor codifica el HTML): acentos y «•» legibles.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <returns>El texto enmascarado y si era JSON (con sangría) o texto plano.</returns>
    public static (string Text, bool IsJson) Format(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return (string.Empty, false);
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(payload);
        }
        catch (JsonException)
        {
            return (MaskDigitRuns(payload, TextDigitRunLength), false);
        }

        if (root is null)
        {
            return ("null", true);
        }

        root = MaskNode(root, key: null);
        return (root?.ToJsonString(Indented) ?? "null", true);
    }

    private static JsonNode? MaskNode(JsonNode node, string? key)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj.ToList())
                {
                    // Solo se reemplaza un valor que cambió (un nodo no puede tener dos padres).
                    if (property.Value is not null && MaskNode(property.Value, property.Key) is var masked
                        && !ReferenceEquals(masked, property.Value))
                    {
                        obj[property.Key] = masked;
                    }
                }

                return obj;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is { } item && MaskNode(item, key) is var masked && !ReferenceEquals(masked, item))
                    {
                        array[i] = masked;
                    }
                }

                return array;
            case JsonValue value:
                return MaskValue(value, key);
            default:
                return node;
        }
    }

    private static JsonNode MaskValue(JsonValue value, string? key)
    {
        var kind = value.GetValueKind();
        if (kind is not (JsonValueKind.String or JsonValueKind.Number))
        {
            return value;
        }

        var text = kind == JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString();
        if (key is not null && ClabeKeys.Contains(key))
        {
            return JsonValue.Create(text.Length == 0 ? text : Clabe.Mask(text))!;
        }

        if (key is not null && PhoneKeys.Contains(key))
        {
            return JsonValue.Create(text.Length == 0 ? text : PhoneNumber.Mask(text))!;
        }

        if (key is not null && TaxIdKeys.Contains(key))
        {
            return JsonValue.Create(text.Length == 0 ? text : "•••• " + (text.Length <= 3 ? text : text[^3..]))!;
        }

        var masked = MaskDigitRuns(text, JsonDigitRunLength);
        if (masked == text)
        {
            return value;
        }

        return JsonValue.Create(masked)!;
    }

    private static string MaskDigitRuns(string text, int minLength) =>
        DigitRun().Replace(text, m => m.Length >= minLength ? Clabe.Mask(m.Value) : m.Value);

    [GeneratedRegex(@"\d+", RegexOptions.CultureInvariant)]
    private static partial Regex DigitRun();
}
