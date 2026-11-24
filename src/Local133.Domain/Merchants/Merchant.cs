using Local133.Domain.BankAccounts;
using Local133.Domain.Common;
using Local133.Domain.Pey;
using Local133.Domain.Subscriptions;

namespace Local133.Domain.Merchants;

/// <summary>
/// Vendedor (tenant). Se crea al registrarse, ligado 1:1 al usuario de Identity, en
/// <see cref="MerchantStatus.PendingBankAccount"/>.
/// </summary>
public class Merchant : Entity
{
    public const int BusinessNameMaxLength = 120;

    /// <summary>Id del usuario de Identity (<c>AspNetUsers.Id</c>).</summary>
    public string UserId { get; set; } = null!;

    public MerchantStatus Status { get; set; } = MerchantStatus.PendingBankAccount;

    /// <summary>Nombre del negocio (se captura en "Mi información").</summary>
    public string? BusinessName { get; set; }

    /// <summary>
    /// Nombre de la persona (<see cref="PersonName"/>), capturado en el alta desde la 4.0. Nulo para los vendedores
    /// registrados antes.
    /// </summary>
    public string? OwnerName { get; set; }

    /// <summary>Dirección o ubicación del puesto (<see cref="MerchantAddress"/>), opcional; renglones con <c>\n</c>.</summary>
    public string? Address { get; set; }

    /// <summary>Llave base del logo en el almacenamiento (PNG; ver <c>MerchantImageRules</c>), o <c>null</c>.</summary>
    public string? LogoKey { get; set; }

    /// <summary>Llave base de la foto del local en el almacenamiento (JPEG), o <c>null</c>.</summary>
    public string? ShopPhotoKey { get; set; }

    public const int ImageKeyMaxLength = 100;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Cuándo se le avisó por correo que su cuenta quedó activa. Es el candado del aviso: el webhook y el sondeo
    /// pueden ver la misma activación a la vez, y solo el primero que lo llena manda el correo.
    /// </summary>
    public DateTimeOffset? ActivationEmailSentAt { get; set; }

    /// <summary>
    /// Código de invitación (<see cref="Merchants.ReferralCode"/>, 5.8), único. Se genera al registrarse y no cambia.
    /// </summary>
    public string ReferralCode { get; set; } = string.Empty;

    /// <summary>
    /// Nombre del catálogo público (<see cref="Merchants.PublicSlug"/>, 5.10), único entre vendedores. Se conserva al
    /// dejar de publicar.
    /// </summary>
    public string? PublicSlug { get; set; }

    /// <summary>El vendedor publicó su catálogo (<c>/tienda/{slug}</c>). Solo se ve si además su plan lo permite.</summary>
    public bool PublicCatalogEnabled { get; set; }

    /// <param name="ownerName">Nombre de la persona; se normaliza con <see cref="PersonName.Normalize"/>.</param>
    /// <param name="referralCode">
    /// Código de invitación ya revisado contra la base (único); sin él se genera uno al azar
    /// (<see cref="Merchants.ReferralCode.Generate"/>).
    /// </param>
    /// <exception cref="ArgumentException">El nombre o el código no son válidos.</exception>
    public static Merchant Register(string userId, DateTimeOffset now, string ownerName, string? referralCode = null)
    {
        if (!PersonName.IsValid(ownerName))
        {
            throw new ArgumentException("El nombre de la persona no es válido.", nameof(ownerName));
        }

        if (referralCode is not null && !Merchants.ReferralCode.IsValid(referralCode))
        {
            throw new ArgumentException("El código de invitación no es válido.", nameof(referralCode));
        }

        return new Merchant
        {
            UserId = userId,
            Status = MerchantStatus.PendingBankAccount,
            OwnerName = PersonName.Normalize(ownerName),
            CreatedAt = now,
            ReferralCode = referralCode ?? Merchants.ReferralCode.Generate(),
        };
    }

    /// <summary>Con quién saludar en Inicio y en los correos (ver <see cref="GreetingNameFor"/>).</summary>
    public string? GreetingName => GreetingNameFor(OwnerName, BusinessName);

    /// <summary>
    /// Saludo: el primer nombre de la persona; sin él, el nombre del negocio; sin ninguno, <c>null</c> (cada pantalla o
    /// correo pone su respaldo). Estático para usarlo con lo que se proyecta de la base.
    /// </summary>
    public static string? GreetingNameFor(string? ownerName, string? businessName) =>
        PersonName.FirstName(ownerName) ?? (string.IsNullOrWhiteSpace(businessName) ? null : businessName.Trim());

    /// <summary>
    /// Puede capturar (o volver a capturar) su CLABE: aún no la registra, o Pey rechazó la que envió.
    /// </summary>
    public bool CanSubmitBankAccount(BankAccount? current) =>
        Status == MerchantStatus.PendingBankAccount && (current is null || current.CodiStatus == PeyCodiStatus.Error);

    /// <summary>
    /// Puede generar cobros: el vendedor está activo y su cuenta bancaria está activa en CoDi (cada cobro se
    /// deposita en ella con su <c>bankAccountId</c>).
    /// </summary>
    public bool CanCharge(BankAccount? account) =>
        Status == MerchantStatus.Active && account is { CodiStatus: PeyCodiStatus.Active };

    /// <summary>
    /// Puede dar de alta, editar y dar de baja productos: cualquier vendedor salvo uno dado de baja. Durante la
    /// revisión de su cuenta ya puede armar su catálogo, y la suspensión (por cobranza) solo frena los cobros.
    /// </summary>
    public bool CanManageProducts => Status != MerchantStatus.Disabled;

    /// <summary>Puede editar «Mi información» (4.1): cualquier vendedor salvo uno dado de baja, en cualquier plan.</summary>
    public bool CanEditProfile => Status != MerchantStatus.Disabled;

    /// <summary>
    /// Guarda los datos generales de «Mi información», normalizados. La dirección vacía se guarda como <c>null</c>.
    /// </summary>
    /// <exception cref="ArgumentException">Algún dato no cumple <see cref="PersonName"/>, <see cref="Merchants.BusinessName"/> o <see cref="MerchantAddress"/>.</exception>
    public void UpdateProfile(string ownerName, string businessName, string? address)
    {
        if (!PersonName.IsValid(ownerName))
        {
            throw new ArgumentException("El nombre de la persona no es válido.", nameof(ownerName));
        }

        if (!Merchants.BusinessName.IsValid(businessName))
        {
            throw new ArgumentException("El nombre del negocio no es válido.", nameof(businessName));
        }

        if (!MerchantAddress.IsValid(address))
        {
            throw new ArgumentException("La dirección no es válida.", nameof(address));
        }

        OwnerName = PersonName.Normalize(ownerName);
        BusinessName = Merchants.BusinessName.Normalize(businessName);
        var normalizedAddress = MerchantAddress.Normalize(address);
        Address = normalizedAddress.Length == 0 ? null : normalizedAddress;
    }

    /// <summary>Cambia el logo (o lo quita con <c>null</c>) y devuelve la llave anterior, para borrar sus archivos.</summary>
    public string? ReplaceLogo(string? key)
    {
        var previous = LogoKey;
        LogoKey = key;
        return previous;
    }

    /// <summary>Cambia la foto del local (o la quita con <c>null</c>) y devuelve la llave anterior.</summary>
    public string? ReplaceShopPhoto(string? key)
    {
        var previous = ShopPhotoKey;
        ShopPhotoKey = key;
        return previous;
    }

    /// <summary>
    /// Publica el catálogo con <paramref name="slug"/> (ya normalizado). La unicidad la revisa quien lo guarda; el plan
    /// y el nombre del negocio, el caso de uso.
    /// </summary>
    /// <exception cref="ArgumentException">El slug no cumple <see cref="Merchants.PublicSlug"/>.</exception>
    public void PublishCatalog(string slug)
    {
        if (!Merchants.PublicSlug.IsValid(slug))
        {
            throw new ArgumentException("El nombre de la liga no es válido.", nameof(slug));
        }

        PublicSlug = slug;
        PublicCatalogEnabled = true;
    }

    /// <summary>Deja de publicar el catálogo. El slug queda apartado para volver a publicarlo.</summary>
    public void UnpublishCatalog() => PublicCatalogEnabled = false;

    /// <summary>
    /// El catálogo público se muestra: está publicado, el plan lo permite (Pro) y el vendedor no está suspendido ni
    /// dado de baja. Si no, la liga dice «Catálogo no disponible por ahora».
    /// </summary>
    public bool CanShowPublicCatalog(PlanEntitlements entitlements) =>
        CanShowPublicCatalog(PublicCatalogEnabled, PublicSlug, Status, entitlements);

    /// <summary>Igual que la de instancia, con lo que se proyecta de la base.</summary>
    public static bool CanShowPublicCatalog(
        bool enabled, string? slug, MerchantStatus status, PlanEntitlements entitlements) =>
        enabled && slug is not null && entitlements.CanPublishCatalog
        && status is not (MerchantStatus.Suspended or MerchantStatus.Disabled);

    /// <summary>
    /// «Mi información» completa para el paso opcional de Inicio: el negocio tiene nombre y hay un teléfono válido
    /// (el celular de la cuenta de Identity).
    /// </summary>
    public bool HasCompleteProfile(string? phoneNumber) =>
        !string.IsNullOrWhiteSpace(BusinessName) && Sales.PhoneNumber.IsValid(phoneNumber);

    /// <summary>
    /// Transición por el estatus CoDi de su cuenta (alta, webhook <c>bank.account.updated</c> o sondeo). Solo aplica
    /// durante el onboarding; un vendedor activo, suspendido o dado de baja no cambia aquí.
    /// Devuelve <c>true</c> si cambió el estatus.
    /// </summary>
    public bool ApplyBankAccountStatus(PeyCodiStatus codiStatus)
    {
        if (Status is not (MerchantStatus.PendingBankAccount or MerchantStatus.BankAccountInReview))
        {
            return false;
        }

        var next = codiStatus switch
        {
            PeyCodiStatus.Pending or PeyCodiStatus.Processing => MerchantStatus.BankAccountInReview,
            PeyCodiStatus.Active => MerchantStatus.Active,
            PeyCodiStatus.Error => MerchantStatus.PendingBankAccount,
            _ => throw new ArgumentOutOfRangeException(nameof(codiStatus), codiStatus, "Estatus CoDi desconocido."),
        };
        if (next == Status)
        {
            return false;
        }

        Status = next;
        return true;
    }
}
