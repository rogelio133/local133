using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;

using Local133.Domain.Pey;

namespace Local133.Infrastructure.Pey;

/// <summary>Serialización del cable de Pey: camelCase, enums en <c>UPPER_SNAKE</c>, sin nulos al escribir.</summary>
internal static class PeyJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false) },
    };

    private static readonly FrozenDictionary<string, PeyErrorType> ErrorTypes = Enum.GetValues<PeyErrorType>()
        .Where(t => t != PeyErrorType.Unknown)
        .ToFrozenDictionary(ToWireName, t => t, StringComparer.OrdinalIgnoreCase);

    /// <summary><c>INVALID_CLABE</c> ⇒ <see cref="PeyErrorType.InvalidClabe"/>; ausente o desconocido ⇒ <see cref="PeyErrorType.Unknown"/>.</summary>
    public static PeyErrorType ParseErrorType(string? value) =>
        value is not null && ErrorTypes.TryGetValue(value.Trim(), out var type) ? type : PeyErrorType.Unknown;

    public static string ToWireName<TEnum>(TEnum value) where TEnum : struct, Enum =>
        JsonNamingPolicy.SnakeCaseUpper.ConvertName(value.ToString());

    /// <summary>Los valores de <c>externalMetadata</c> vuelven como texto; por si acaso se acepta cualquier escalar.</summary>
    public static string? ScalarToString(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => element.GetRawText(),
    };
}
