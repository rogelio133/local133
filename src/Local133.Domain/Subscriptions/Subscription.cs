using Local133.Domain.Common;
using Local133.Domain.Settings;

namespace Local133.Domain.Subscriptions;

/// <summary>Lo que hizo <see cref="Subscription.ConvertBasicToPro"/>: el tiempo de Básico que quedaba y el de Pro que dio.</summary>
public sealed record BasicToProConversion(TimeSpan BasicTime, TimeSpan ProTime)
{
    public static BasicToProConversion None { get; } = new(TimeSpan.Zero, TimeSpan.Zero);
}

/// <summary>
/// Suscripción del vendedor (una por vendedor). Nace con la prueba. Los días de cada plan están en <see cref="Periods"/>;
/// el estado del plan se calcula con ellos (<see cref="PlanStatus.For"/>). Para agregar periodos hay que cargar
/// <see cref="Periods"/>.
/// </summary>
public class Subscription : Entity
{
    public int MerchantId { get; set; }

    /// <summary>Inicio de la prueba (una sola vez por vendedor).</summary>
    public DateTimeOffset TrialStartedAt { get; set; }

    /// <summary>
    /// Vigente hasta (excluido): el fin del último periodo, de cualquier plan, guardado para consultar rápido. Conserva
    /// el nombre de cuando solo había Pro.
    /// </summary>
    public DateTimeOffset ProUntil { get; set; }

    /// <summary>Origen del último periodo.</summary>
    public SubscriptionPeriodKind LastPeriodKind { get; set; }

    /// <summary>Plan del último pago (junto con <see cref="LastPackage"/>): «Renovar igual que la última vez».</summary>
    public SubscriptionPlan? LastPlan { get; set; }

    /// <summary>Paquete del último pago: define el link de renovación y los recordatorios (5.6, 5.7).</summary>
    public SubscriptionPackage? LastPackage { get; set; }

    /// <summary>Tiene precio de fundador, en los dos planes (5.6).</summary>
    public bool IsFounder { get; set; }

    /// <summary>Lugar de fundador (1 a <c>FounderSlots</c>), único.</summary>
    public int? FounderNumber { get; set; }

    /// <summary>
    /// Precio del último anual pagado, del plan <see cref="LockedAnnualPlan"/>: se respeta al renovar el anual del mismo
    /// plan sin haber vencido (5.6).
    /// </summary>
    public decimal? LockedAnnualPrice { get; set; }

    /// <summary>Plan de <see cref="LockedAnnualPrice"/>.</summary>
    public SubscriptionPlan? LockedAnnualPlan { get; set; }

    public List<SubscriptionPeriod> Periods { get; set; } = [];

    /// <summary>
    /// Plan del último periodo de la cadena, donde se encadena un premio o un regalo (§8). La prueba es de Pro.
    /// </summary>
    public SubscriptionPlan ChainEndPlan =>
        Periods.OrderBy(p => p.EndsAt).ThenBy(p => p.CreatedAt).LastOrDefault()?.Plan ?? SubscriptionPlan.Pro;

    /// <summary>Crea la suscripción con la prueba de Pro de <paramref name="trialDays"/> días desde <paramref name="now"/>.</summary>
    public static Subscription StartTrial(int merchantId, DateTimeOffset now, int trialDays)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(trialDays);
        var trial = SubscriptionPeriod.Create(
            merchantId, SubscriptionPeriodKind.Trial, SubscriptionPlan.Pro, now, MexicoTime.AddDays(now, trialDays), now);
        return new Subscription
        {
            MerchantId = merchantId,
            TrialStartedAt = now,
            ProUntil = trial.EndsAt,
            LastPeriodKind = SubscriptionPeriodKind.Trial,
            Periods = [trial],
        };
    }

    public PlanStatus StatusAt(DateTimeOffset now, int graceDays) => PlanStatus.For(Periods, now, graceDays);

    /// <summary>
    /// Periodo pagado con un paquete de un plan; empieza según <see cref="PlanStatus.NextPeriodStart"/>. Pagar otro plan
    /// suelta el anual congelado. Para subir de Básico a Pro, quien llama primero usa <see cref="ConvertBasicToPro"/>.
    /// </summary>
    /// <param name="subscriptionChargeId">Cargo que lo pagó (5.6).</param>
    public SubscriptionPeriod AddPaidPeriod(
        SubscriptionPlan plan, SubscriptionPackage package, DateTimeOffset now, int graceDays, int? subscriptionChargeId = null)
    {
        var start = PlanStatus.NextPeriodStart(Periods, now, graceDays);
        var period = Add(SubscriptionPeriodKind.Paid, plan, start, MexicoTime.AddMonths(start, package.Months()), now);
        period.SubscriptionChargeId = subscriptionChargeId;
        LastPlan = plan;
        LastPackage = package;
        if (LockedAnnualPlan is { } lockedPlan && lockedPlan != plan)
        {
            LockedAnnualPrice = null;
            LockedAnnualPlan = null;
        }

        return period;
    }

    /// <summary>
    /// Precio de un paquete de un plan para este vendedor (§2 y §8 del diseño): de fundador si ya lo es o si
    /// <paramref name="founderSlotAvailable"/>, el anual congelado solo si es del mismo plan y no ha vencido (sigue en
    /// prueba, Básico, Pro o gracia), y el descuento de bienvenida si <paramref name="welcomeDiscount"/>.
    /// </summary>
    /// <param name="welcomeDiscount">
    /// Se registró con un código y todavía no se le abona ningún pago (5.8): 30 % de un mes del plan, en cualquier paquete.
    /// </param>
    /// <returns>La cotización y si salió con precio de fundador.</returns>
    public (SubscriptionQuote Quote, bool IsFounderPrice) QuoteFor(
        SubscriptionPlan plan, SubscriptionPackage package, SystemSettings settings, PlanStatus status,
        bool founderSlotAvailable, bool welcomeDiscount = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(status);
        var founder = IsFounder || founderSlotAvailable;
        var locked = status.IsCurrent && LockedAnnualPlan == plan ? LockedAnnualPrice : null;
        return (SubscriptionPricing.Quote(plan, package, settings, founder, locked, welcomeDiscount), founder);
    }

    /// <summary>Lugar de fundador (precio de fundador de por vida, en los dos planes). Una sola vez.</summary>
    public void AssignFounder(int number)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        if (IsFounder)
        {
            throw new InvalidOperationException("El vendedor ya es fundador.");
        }

        IsFounder = true;
        FounderNumber = number;
    }

    /// <summary>Precio del anual que acaba de pagar: se respeta al renovar el anual del mismo plan sin haber vencido.</summary>
    public void LockAnnualPrice(SubscriptionPlan plan, decimal price)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(price);
        LockedAnnualPrice = price;
        LockedAnnualPlan = plan;
    }

    /// <summary>
    /// Periodo de días de un plan (premio de referido, regalo del admin o transición); empieza según
    /// <see cref="PlanStatus.NextPeriodStart"/>. No sirve para la prueba, que solo se da con <see cref="StartTrial"/>, ni
    /// para la conversión, que solo da <see cref="ConvertBasicToPro"/>.
    /// </summary>
    /// <param name="referralId">Referido que lo ganó; solo en <see cref="SubscriptionPeriodKind.ReferralReward"/> (5.8).</param>
    public SubscriptionPeriod AddDaysPeriod(
        SubscriptionPeriodKind kind, SubscriptionPlan plan, int days, DateTimeOffset now, int graceDays, int? referralId = null)
    {
        if (kind is not (SubscriptionPeriodKind.ReferralReward or SubscriptionPeriodKind.AdminGrant or SubscriptionPeriodKind.Transition))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Solo los premios, los regalos y la transición se dan por días.");
        }

        if (referralId is not null && kind != SubscriptionPeriodKind.ReferralReward)
        {
            throw new ArgumentException("Solo un premio de referido lleva el referido.", nameof(referralId));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(days);
        var start = PlanStatus.NextPeriodStart(Periods, now, graceDays);
        var period = Add(kind, plan, start, MexicoTime.AddDays(start, days), now);
        period.ReferralId = referralId;
        return period;
    }

    /// <summary>
    /// Sube de Básico a Pro (§3 del diseño): el tiempo de Básico que le queda desde <paramref name="now"/> se vuelve
    /// tiempo de Pro en proporción <paramref name="ratio"/> (<see cref="SubscriptionPricing.ConversionRatio"/>), y lo
    /// que sigue se recorre hacia atrás para no dejar hueco. Recorre la cadena en orden:
    /// <list type="bullet">
    /// <item>un Básico en curso se corta en <paramref name="now"/> (lo usado sigue siendo Básico) y lo que le quedaba,
    /// encogido, pasa a un periodo <see cref="SubscriptionPeriodKind.Conversion"/> de Pro nuevo;</item>
    /// <item>un Básico que no ha empezado se vuelve <c>Conversion</c> de Pro en su lugar, encogido, y conserva su cargo
    /// o su referido;</item>
    /// <item>lo demás conserva su duración.</item>
    /// </list>
    /// Las duraciones encogidas se redondean hacia abajo al segundo. Lo pagado de Pro se encadena después, con
    /// <see cref="AddPaidPeriod"/>. Sin Básico por delante (también en la gracia de Básico) no cambia nada.
    /// </summary>
    public BasicToProConversion ConvertBasicToPro(DateTimeOffset now, decimal ratio)
    {
        if (ratio <= 0 || ratio > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ratio), ratio, "La proporción va de 0 (excluido) a 1.");
        }

        var ahead = Periods
            .Where(p => p.EndsAt > now)
            .OrderBy(p => p.StartsAt)
            .ThenBy(p => p.CreatedAt)
            .ToList();
        if (!ahead.Any(p => p.Plan == SubscriptionPlan.Basic))
        {
            return BasicToProConversion.None;
        }

        var basicTime = TimeSpan.Zero;
        var proTime = TimeSpan.Zero;
        DateTimeOffset? cursor = null;
        foreach (var period in ahead)
        {
            if (period.StartsAt < now && period.Plan != SubscriptionPlan.Basic)
            {
                // En curso y no es Básico: se queda como está.
                cursor = cursor is { } end && end > period.EndsAt ? end : period.EndsAt;
                continue;
            }

            var from = period.StartsAt > now ? period.StartsAt : now;
            var start = cursor ?? from;
            var length = period.EndsAt - from;
            if (period.Plan == SubscriptionPlan.Basic)
            {
                var scaled = Scale(length, ratio);
                basicTime += length;
                proTime += scaled;
                if (period.StartsAt < now)
                {
                    period.EndsAt = now;
                    var conversion = SubscriptionPeriod.Create(
                        MerchantId, SubscriptionPeriodKind.Conversion, SubscriptionPlan.Pro, now, now + scaled, now);
                    Periods.Add(conversion);
                    cursor = conversion.EndsAt;
                    continue;
                }

                period.Kind = SubscriptionPeriodKind.Conversion;
                period.Plan = SubscriptionPlan.Pro;
                length = scaled;
            }

            period.StartsAt = start;
            period.EndsAt = start + length;
            cursor = cursor is { } current && current > period.EndsAt ? current : period.EndsAt;
        }

        RefreshTail();
        return new BasicToProConversion(basicTime, proTime);
    }

    /// <summary>Tiempo de Básico que le queda desde <paramref name="now"/> («Tus 20 días de Básico…»).</summary>
    public TimeSpan BasicTimeAhead(DateTimeOffset now) =>
        Periods
            .Where(p => p.Plan == SubscriptionPlan.Basic && p.EndsAt > now)
            .Aggregate(TimeSpan.Zero, (sum, p) => sum + (p.EndsAt - (p.StartsAt > now ? p.StartsAt : now)));

    /// <summary>
    /// Dónde empezaría lo pagado de <paramref name="plan"/> si se abonara en <paramref name="now"/>, sin cambiar nada:
    /// en Pro, después de convertir el Básico que tiene por delante (<see cref="ConvertBasicToPro"/>).
    /// </summary>
    public DateTimeOffset NextPeriodStartFor(SubscriptionPlan plan, DateTimeOffset now, int graceDays, decimal ratio)
    {
        if (plan == SubscriptionPlan.Basic || BasicTimeAhead(now) == TimeSpan.Zero)
        {
            return PlanStatus.NextPeriodStart(Periods, now, graceDays);
        }

        var copy = new Subscription
        {
            MerchantId = MerchantId,
            ProUntil = ProUntil,
            LastPeriodKind = LastPeriodKind,
            Periods = Periods.Select(p => p.Copy()).ToList(),
        };
        copy.ConvertBasicToPro(now, ratio);
        return PlanStatus.NextPeriodStart(copy.Periods, now, graceDays);
    }

    /// <summary>
    /// Quita los días que todavía no se usan de un premio de referido que el admin anuló (6.2): todo el periodo si no
    /// ha empezado (<c>RemovePeriod</c>: quien llama lo borra de la base), o desde <paramref name="now"/> si está en
    /// curso. Los periodos que venían después se recorren hacia atrás por la misma duración, para no dejar un hueco.
    /// Los días ya usados no se recuperan: si el premio ya terminó, no cambia nada. También vale para un premio de
    /// Básico que se volvió Pro al subir de plan (<c>Conversion</c> con el referido).
    /// </summary>
    /// <returns>Lo que se quitó y si el periodo se quitó completo.</returns>
    public (TimeSpan Removed, bool RemovePeriod) RemoveUnusedDays(SubscriptionPeriod period, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(period);
        var isReward = period.Kind == SubscriptionPeriodKind.ReferralReward
            || (period.Kind == SubscriptionPeriodKind.Conversion && period.ReferralId is not null);
        if (!isReward)
        {
            throw new ArgumentException("Solo se quitan los días de un premio de referido.", nameof(period));
        }

        if (!Periods.Contains(period))
        {
            throw new ArgumentException("El periodo no es de esta suscripción.", nameof(period));
        }

        var from = period.StartsAt > now ? period.StartsAt : now;
        var originalEnd = period.EndsAt;
        if (from >= originalEnd)
        {
            return (TimeSpan.Zero, false);
        }

        var removed = originalEnd - from;
        var removePeriod = from == period.StartsAt;
        if (removePeriod)
        {
            Periods.Remove(period);
        }
        else
        {
            period.EndsAt = from;
        }

        foreach (var later in Periods.Where(p => p != period && p.StartsAt >= originalEnd))
        {
            later.StartsAt -= removed;
            later.EndsAt -= removed;
        }

        RefreshTail();
        return (removed, removePeriod);
    }

    private SubscriptionPeriod Add(
        SubscriptionPeriodKind kind, SubscriptionPlan plan, DateTimeOffset start, DateTimeOffset end, DateTimeOffset now)
    {
        var period = SubscriptionPeriod.Create(MerchantId, kind, plan, start, end, now);
        Periods.Add(period);
        if (period.EndsAt >= ProUntil)
        {
            ProUntil = period.EndsAt;
            LastPeriodKind = kind;
        }

        return period;
    }

    /// <summary>Recalcula <see cref="ProUntil"/> y <see cref="LastPeriodKind"/> con el último periodo.</summary>
    private void RefreshTail()
    {
        var last = Periods
            .OrderBy(p => p.EndsAt)
            .ThenBy(p => p.CreatedAt)
            .Last();
        ProUntil = last.EndsAt;
        LastPeriodKind = last.Kind;
    }

    /// <summary>Encoge una duración por <paramref name="ratio"/>, hacia abajo al segundo (al menos un segundo).</summary>
    private static TimeSpan Scale(TimeSpan length, decimal ratio)
    {
        var seconds = decimal.Floor(length.Ticks / (decimal)TimeSpan.TicksPerSecond * ratio);
        return TimeSpan.FromTicks((long)Math.Max(1m, seconds) * TimeSpan.TicksPerSecond);
    }
}
