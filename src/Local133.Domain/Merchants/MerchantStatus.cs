namespace Local133.Domain.Merchants;

/// <summary>Estatus del vendedor en la plataforma.</summary>
public enum MerchantStatus
{
    /// <summary>Recién registrado: aún no da de alta su cuenta bancaria.</summary>
    PendingBankAccount,

    /// <summary>Cuenta bancaria enviada a Pey; Banxico la está validando (hasta 5 días hábiles).</summary>
    BankAccountInReview,

    /// <summary>Puede generar cobros.</summary>
    Active,

    /// <summary>Suspendido (estado de cuenta sin pagar, decisión del admin o error en la cuenta bancaria).</summary>
    Suspended,

    /// <summary>Dado de baja.</summary>
    Disabled,
}
