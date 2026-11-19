namespace Local133.Domain.Pey;

/// <summary>Restricciones de la API de Pey (OpenAPI 2026-10-20). Se validan antes de llamar.</summary>
public static class PeyLimits
{
    /// <summary>Expiración mínima (segundos) de un QR con expiración y de una notificación push.</summary>
    public const int MinExpirationSeconds = 360;

    /// <summary>Expiración que significa "no expira" (solo QR).</summary>
    public const int NoExpiration = 0;

    /// <summary><c>numRef</c>: hasta 7 dígitos.</summary>
    public const int MaxNumRef = 9_999_999;

    public const int MaxMetadataPairs = 50;
    public const int MaxMetadataKeyLength = 40;
    public const int MaxMetadataValueLength = 500;

    /// <summary>Monto máximo de una notificación push (MXN).</summary>
    public const decimal MaxPushAmount = 1_000_000m;

    /// <summary>Decimales permitidos en los montos (centavos).</summary>
    public const int AmountDecimals = 2;

    public const int PhoneNumberLength = 10;
    public const int ClabeLength = 18;

    /// <summary>Longitud máxima de <c>alias</c> y <c>name</c> de la cuenta bancaria.</summary>
    public const int BankAccountTextMaxLength = 255;

    /// <summary>Longitud máxima de <c>clientReferenceId</c> de un link de pago.</summary>
    public const int MaxClientReferenceIdLength = 255;
}
