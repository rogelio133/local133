using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Local133.Infrastructure.Webhooks;

/// <summary>Resultado de verificar la firma de un webhook.</summary>
public enum WebhookSignatureStatus
{
    Valid,

    /// <summary>Falta <c>webhook-id</c>, <c>webhook-timestamp</c> o <c>webhook-signature</c>.</summary>
    MissingHeaders,

    /// <summary>El timestamp no es un número o está fuera de la tolerancia (posible repetición).</summary>
    InvalidTimestamp,

    /// <summary>Ninguna firma <c>v1</c> coincide con el body.</summary>
    InvalidSignature,
}

public readonly record struct WebhookSignatureResult(WebhookSignatureStatus Status, DateTimeOffset Timestamp)
{
    public bool IsValid => Status == WebhookSignatureStatus.Valid;
}

/// <summary>
/// Verificación de firmas <see href="https://www.standardwebhooks.com/">Standard Webhooks</see> (las que usa Pey):
/// <c>webhook-signature</c> = lista separada por espacios de <c>v1,&lt;base64(HMAC-SHA256(secreto, "{id}.{timestamp}.{body}"))&gt;</c>;
/// el secreto es <c>whsec_&lt;base64&gt;</c>. Se firma el body crudo: no se debe volver a serializar.
/// </summary>
public static class StandardWebhookVerifier
{
    public const string IdHeader = "webhook-id";
    public const string TimestampHeader = "webhook-timestamp";
    public const string SignatureHeader = "webhook-signature";

    /// <summary>Tolerancia del timestamp contra el reloj propio (la del estándar; Pey no documenta otra).</summary>
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    private const string SecretPrefix = "whsec_";
    private const string SignatureVersion = "v1";

    public static WebhookSignatureResult Verify(
        string secret, string? webhookId, string? timestamp, string? signatures, string body, DateTimeOffset now,
        TimeSpan? tolerance = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (string.IsNullOrWhiteSpace(webhookId) || string.IsNullOrWhiteSpace(timestamp) || string.IsNullOrWhiteSpace(signatures))
        {
            return new(WebhookSignatureStatus.MissingHeaders, default);
        }

        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return new(WebhookSignatureStatus.InvalidTimestamp, default);
        }

        var sentAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
        if ((now - sentAt).Duration() > (tolerance ?? DefaultTolerance))
        {
            return new(WebhookSignatureStatus.InvalidTimestamp, sentAt);
        }

        var expected = Sign(SecretKey(secret), webhookId, timestamp, body);
        foreach (var candidate in signatures.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var comma = candidate.IndexOf(',');
            if (comma <= 0 || candidate[..comma] != SignatureVersion)
            {
                continue;
            }

            var provided = new byte[expected.Length];
            if (Convert.TryFromBase64String(candidate[(comma + 1)..], provided, out var written)
                && written == expected.Length
                && CryptographicOperations.FixedTimeEquals(provided, expected))
            {
                return new(WebhookSignatureStatus.Valid, sentAt);
            }
        }

        return new(WebhookSignatureStatus.InvalidSignature, sentAt);
    }

    /// <summary>Firma <c>v1,…</c> de un mensaje (pruebas y herramientas locales que simulan a Pey).</summary>
    public static string ComputeSignature(string secret, string webhookId, string timestamp, string body) =>
        $"{SignatureVersion},{Convert.ToBase64String(Sign(SecretKey(secret), webhookId, timestamp, body))}";

    /// <summary>Llave del secreto: base64 tras el prefijo <c>whsec_</c> (también se acepta sin prefijo).</summary>
    private static byte[] SecretKey(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        var value = secret.Trim();
        if (value.StartsWith(SecretPrefix, StringComparison.Ordinal))
        {
            value = value[SecretPrefix.Length..];
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("El secreto del webhook de Pey (Pey:WebhookSecret) no es whsec_<base64>.", ex);
        }
    }

    private static byte[] Sign(byte[] key, string webhookId, string timestamp, string body) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{webhookId}.{timestamp}.{body}"));
}
