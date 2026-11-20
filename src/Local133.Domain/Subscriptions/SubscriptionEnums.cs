namespace Local133.Domain.Subscriptions;

/// <summary>
/// Estado del plan del vendedor. Se calcula con la fecha y la historia de periodos (<see cref="PlanStatus.For"/>);
/// no se guarda. <c>MerchantStatus</c> manda sobre el plan: un vendedor suspendido o dado de baja no cobra en ninguno.
/// </summary>
public enum PlanTier
{
    /// <summary>Aún no tiene la cuenta bancaria activa (no ha empezado la prueba).</summary>
    Onboarding,

    /// <summary>Dentro de la prueba de Pro.</summary>
    Trial,

    /// <summary>Dentro de un periodo pagado o regalado (premio de referido o admin).</summary>
    Pro,

    /// <summary>Días de gracia después de vencer un periodo <b>pagado</b>: conserva Pro con aviso.</summary>
    Grace,

    /// <summary>Plan gratuito, con límites.</summary>
    Free,
}

/// <summary>Paquetes de pago adelantado de Pro.</summary>
public enum SubscriptionPackage
{
    OneMonth,
    ThreeMonths,
    SixMonths,
    Annual,
}

/// <summary>Origen de un periodo de Pro.</summary>
public enum SubscriptionPeriodKind
{
    /// <summary>Prueba, una sola vez por vendedor, al activarse su cuenta bancaria.</summary>
    Trial,

    /// <summary>Pagado con el link de pago (5.6).</summary>
    Paid,

    /// <summary>Premio por un referido que pagó (5.8).</summary>
    ReferralReward,

    /// <summary>Regalo del admin (Fase 6).</summary>
    AdminGrant,
}

/// <summary>Estatus de un cargo de la suscripción (un link de pago por cargo, 5.6).</summary>
public enum SubscriptionChargeStatus
{
    Pending,
    Processing,
    Paid,
    Partial,
    Failed,
}

/// <summary>Motivo de un descuento en el precio de un cargo.</summary>
public enum SubscriptionDiscountReason
{
    /// <summary>Bienvenida del referido en su primer pago (5.8).</summary>
    ReferralWelcome,
}

public static class SubscriptionPackageExtensions
{
    /// <summary>Meses que cubre el paquete.</summary>
    public static int Months(this SubscriptionPackage package) => package switch
    {
        SubscriptionPackage.OneMonth => 1,
        SubscriptionPackage.ThreeMonths => 3,
        SubscriptionPackage.SixMonths => 6,
        SubscriptionPackage.Annual => 12,
        _ => throw new ArgumentOutOfRangeException(nameof(package), package, "Paquete desconocido."),
    };
}
