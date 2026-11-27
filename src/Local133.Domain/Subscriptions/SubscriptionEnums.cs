namespace Local133.Domain.Subscriptions;

/// <summary>
/// Estado del plan del vendedor. Se calcula con la fecha y la historia de periodos (<see cref="PlanStatus.For"/>);
/// no se guarda. <c>MerchantStatus</c> manda sobre el plan: un vendedor suspendido o dado de baja no cobra en ninguno.
/// </summary>
public enum PlanTier
{
    /// <summary>Todavía no empieza la prueba.</summary>
    Onboarding,

    /// <summary>Dentro de la prueba de Pro.</summary>
    Trial,

    /// <summary>Dentro de un periodo de Básico (pagado, regalado, premio o transición).</summary>
    Basic,

    /// <summary>Dentro de un periodo de Pro (pagado, regalado, premio o conversión).</summary>
    Pro,

    /// <summary>Días de gracia después de vencer un periodo <b>pagado</b>: conserva las funciones del plan que pagó, con aviso.</summary>
    Grace,

    /// <summary>Venció la prueba o lo pagado: no genera cobros hasta que pague (2026-11-26).</summary>
    Expired,
}

/// <summary>Plan de un periodo o de un cargo (2026-11-26). La prueba es de Pro.</summary>
public enum SubscriptionPlan
{
    Basic,
    Pro,
}

/// <summary>Paquetes de pago adelantado (de Básico o de Pro).</summary>
public enum SubscriptionPackage
{
    OneMonth,
    ThreeMonths,
    SixMonths,
    Annual,
}

/// <summary>Origen de un periodo del plan.</summary>
public enum SubscriptionPeriodKind
{
    /// <summary>Prueba de Pro, una sola vez por vendedor.</summary>
    Trial,

    /// <summary>Pagado con el link de pago (5.6).</summary>
    Paid,

    /// <summary>Premio por un referido que pagó (5.8).</summary>
    ReferralReward,

    /// <summary>Regalo del admin (Fase 6).</summary>
    AdminGrant,

    /// <summary>
    /// Días de Básico que se volvieron Pro al subir de plan (<see cref="Subscription.ConvertBasicToPro"/>). Sin gracia.
    /// </summary>
    Conversion,

    /// <summary>Días de Básico que dio la migración del cambio de planes (2026-12) a los vendedores existentes. Sin gracia.</summary>
    Transition,
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

public static class SubscriptionPlanExtensions
{
    /// <summary>Nombre en pantalla y en correos: «Básico», «Pro».</summary>
    public static string Label(this SubscriptionPlan plan) => plan switch
    {
        SubscriptionPlan.Basic => "Básico",
        SubscriptionPlan.Pro => "Pro",
        _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, "Plan desconocido."),
    };
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

    /// <summary>Nombre en pantalla y en correos: «1 mes», «3 meses», «6 meses», «Anual».</summary>
    public static string Label(this SubscriptionPackage package) => package switch
    {
        SubscriptionPackage.OneMonth => "1 mes",
        SubscriptionPackage.ThreeMonths => "3 meses",
        SubscriptionPackage.SixMonths => "6 meses",
        SubscriptionPackage.Annual => "Anual",
        _ => throw new ArgumentOutOfRangeException(nameof(package), package, "Paquete desconocido."),
    };
}
