using Local133.Domain.Common;
using Local133.Domain.Merchants;

namespace Local133.Domain.Subscriptions;

/// <summary>Por qué un referido que pagó no le dio premio a quien lo invitó.</summary>
public enum ReferralSkipReason
{
    /// <summary>Quien invitó ya tenía <c>ReferralRewardsPerYear</c> premios en los últimos 12 meses.</summary>
    RewardCap,

    /// <summary>Quien invitó está dado de baja.</summary>
    ReferrerDisabled,
}

/// <summary>
/// Un vendedor que se registró con el código de otro (5.8, §6 del diseño). Uno por referido. Califica con su primer pago
/// abonado; en ese momento quien lo invitó gana sus días de Pro (<see cref="RewardedAt"/>, con el periodo
/// <see cref="SubscriptionPeriodKind.ReferralReward"/> que apunta aquí por <c>SubscriptionPeriod.ReferralId</c>), se
/// omite el premio (<see cref="SkippedReason"/>) o queda pendiente si quien invitó todavía no tiene suscripción.
/// </summary>
public class Referral : Entity
{
    public int ReferrerMerchantId { get; set; }

    public int ReferredMerchantId { get; set; }

    /// <summary>Código con el que se registró (copia del de quien invitó).</summary>
    public string Code { get; set; } = string.Empty;

    public DateTimeOffset RegisteredAt { get; set; }

    /// <summary>Primer pago abonado del referido. Desde aquí ya no tiene descuento de bienvenida.</summary>
    public DateTimeOffset? QualifiedAt { get; set; }

    /// <summary>Cuándo se le dieron los días a quien invitó (cuenta para el tope).</summary>
    public DateTimeOffset? RewardedAt { get; set; }

    public ReferralSkipReason? SkippedReason { get; set; }

    /// <summary>El admin anuló el premio (6.2): ya no se da, y si se dio, ya no cuenta para el tope.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Calificó y su premio espera a que quien invitó tenga suscripción (empiece su prueba).</summary>
    public bool IsPendingReward => QualifiedAt is not null && RewardedAt is null && SkippedReason is null && RevokedAt is null;

    /// <summary>Se dio el premio y no se ha anulado.</summary>
    public bool IsRewardActive => RewardedAt is not null && RevokedAt is null;

    /// <summary>El admin puede anularlo: premio pendiente u otorgado y sin anular.</summary>
    public bool CanBeRevoked => IsPendingReward || IsRewardActive;

    public static Referral Create(int referrerMerchantId, int referredMerchantId, string code, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(referrerMerchantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(referredMerchantId);
        if (referrerMerchantId == referredMerchantId)
        {
            throw new ArgumentException("Un vendedor no se puede referir a sí mismo.", nameof(referredMerchantId));
        }

        if (!ReferralCode.IsValid(code))
        {
            throw new ArgumentException("El código de invitación no es válido.", nameof(code));
        }

        return new Referral
        {
            ReferrerMerchantId = referrerMerchantId,
            ReferredMerchantId = referredMerchantId,
            Code = code,
            RegisteredAt = now,
        };
    }

    /// <summary>Primer pago abonado. Devuelve <c>false</c> si ya había calificado (un segundo pago no cuenta).</summary>
    public bool Qualify(DateTimeOffset now)
    {
        if (QualifiedAt is not null)
        {
            return false;
        }

        QualifiedAt = now;
        return true;
    }

    public void MarkRewarded(DateTimeOffset now)
    {
        if (!IsPendingReward)
        {
            throw new InvalidOperationException("Solo un referido calificado y sin premio puede premiarse.");
        }

        RewardedAt = now;
    }

    public void Skip(ReferralSkipReason reason)
    {
        if (!IsPendingReward)
        {
            throw new InvalidOperationException("Solo un referido calificado y sin premio puede omitirse.");
        }

        SkippedReason = reason;
    }

    /// <summary>
    /// El admin anula el premio (6.2). Si estaba pendiente, ya no se dará; si se dio, quien llama quita los días que no
    /// se han usado (<c>Subscription.RemoveUnusedDays</c>).
    /// </summary>
    public void Revoke(DateTimeOffset now)
    {
        if (!CanBeRevoked)
        {
            throw new InvalidOperationException("Solo se anula un premio pendiente u otorgado que no se haya anulado.");
        }

        RevokedAt = now;
    }
}
