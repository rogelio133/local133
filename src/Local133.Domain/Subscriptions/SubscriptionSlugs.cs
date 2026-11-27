namespace Local133.Domain.Subscriptions;

/// <summary>
/// Nombres del plan y del paquete en las URL del checkout (5.15, §6 del diseño): <c>/app/plan/checkout/pro/anual</c>.
/// Exactos y en minúsculas: cualquier otra forma no existe (404).
/// </summary>
public static class SubscriptionSlugs
{
    public const string Basic = "basico";
    public const string Pro = "pro";

    public static string Slug(this SubscriptionPlan plan) => plan switch
    {
        SubscriptionPlan.Basic => Basic,
        SubscriptionPlan.Pro => Pro,
        _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, "Plan desconocido."),
    };

    public static string Slug(this SubscriptionPackage package) => package switch
    {
        SubscriptionPackage.OneMonth => "1-mes",
        SubscriptionPackage.ThreeMonths => "3-meses",
        SubscriptionPackage.SixMonths => "6-meses",
        SubscriptionPackage.Annual => "anual",
        _ => throw new ArgumentOutOfRangeException(nameof(package), package, "Paquete desconocido."),
    };

    public static bool TryParsePlan(string? slug, out SubscriptionPlan plan)
    {
        foreach (var value in Enum.GetValues<SubscriptionPlan>())
        {
            if (string.Equals(slug, value.Slug(), StringComparison.Ordinal))
            {
                plan = value;
                return true;
            }
        }

        plan = default;
        return false;
    }

    public static bool TryParsePackage(string? slug, out SubscriptionPackage package)
    {
        foreach (var value in Enum.GetValues<SubscriptionPackage>())
        {
            if (string.Equals(slug, value.Slug(), StringComparison.Ordinal))
            {
                package = value;
                return true;
            }
        }

        package = default;
        return false;
    }
}
