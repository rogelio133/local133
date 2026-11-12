using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Local133.Domain.BankAccounts;
using Local133.Domain.Merchants;
using Local133.Domain.Pey;
using Local133.Web.Validation;

namespace Local133.Web.Areas.Merchant.Models;

/// <summary>Formulario de la cuenta bancaria (validación de apoyo en cliente; la definitiva es FluentValidation).</summary>
public sealed class BankAccountInput
{
    [Required(ErrorMessage = "Elige el tipo de titular.")]
    [Display(Name = "Tipo de titular")]
    public PeyEntity? Entity { get; set; }

    [Required(ErrorMessage = "Escribe el nombre del titular.")]
    [StringLength(BankAccount.HolderNameMaxLength, ErrorMessage = "El nombre del titular admite hasta {1} caracteres.")]
    [Display(Name = "Nombre del titular")]
    public string? HolderName { get; set; }

    [Required(ErrorMessage = "Escribe tu CLABE.")]
    [StringLength(ClabeInputMaxLength, ErrorMessage = "La CLABE tiene 18 dígitos.")]
    [Clabe]
    [Display(Name = "CLABE interbancaria")]
    public string? Clabe { get; set; }

    [Required(ErrorMessage = "Escribe el nombre de tu negocio.")]
    [StringLength(BankAccount.AliasMaxLength, ErrorMessage = "El nombre del negocio admite hasta {1} caracteres.")]
    [Display(Name = "Nombre de tu negocio")]
    public string? Alias { get; set; }

    /// <summary>18 dígitos más espacios o guiones entre grupos.</summary>
    public const int ClabeInputMaxLength = 30;
}

/// <summary>Pantalla del formulario (alta o reintento tras un rechazo).</summary>
public sealed class BankAccountFormViewModel
{
    public BankAccountInput Input { get; set; } = new();

    /// <summary>Motivo del rechazo de la cuenta anterior (reintento), si lo hay.</summary>
    public string? PreviousError { get; init; }

    public string? PreviousMaskedClabe { get; init; }

    public bool IsRetry => PreviousMaskedClabe is not null;
}

/// <summary>Estatus de la cuenta registrada (o aviso si no hay cuenta y no puede registrarla).</summary>
public sealed record BankAccountStatusViewModel(
    MerchantStatus MerchantStatus,
    PeyCodiStatus? CodiStatus,
    string? HolderName,
    string? Alias,
    string? EntityLabel,
    string? MaskedClabe,
    string? SubmittedOn,
    string? ErrorMessage)
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-MX");
    private static readonly TimeZoneInfo MexicoCity = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");

    public static BankAccountStatusViewModel From(Domain.Merchants.Merchant merchant, BankAccount? account) => account is null
        ? new(merchant.Status, null, null, null, null, null, null, null)
        : new(
            merchant.Status,
            account.CodiStatus,
            account.HolderName,
            account.Alias,
            EntityLabelOf(account.Entity),
            Clabe.Mask(account.Clabe),
            TimeZoneInfo.ConvertTime(account.SubmittedAt, MexicoCity).ToString("d 'de' MMMM 'de' yyyy", Spanish),
            account.ErrorMessage);

    public static string EntityLabelOf(PeyEntity entity) => entity switch
    {
        PeyEntity.Person => "Persona física",
        PeyEntity.Business => "Persona moral (empresa)",
        _ => entity.ToString(),
    };

    public string StatusLabel => CodiStatus switch
    {
        PeyCodiStatus.Pending or PeyCodiStatus.Processing => "En revisión de Banxico",
        PeyCodiStatus.Active => "Activa",
        PeyCodiStatus.Error => "Rechazada",
        _ => "Sin registrar",
    };

    public string BadgeClass => CodiStatus switch
    {
        PeyCodiStatus.Pending or PeyCodiStatus.Processing => "text-bg-warning",
        PeyCodiStatus.Active => "text-bg-success",
        PeyCodiStatus.Error => "text-bg-danger",
        _ => "text-bg-secondary",
    };

    public string Description => CodiStatus switch
    {
        PeyCodiStatus.Pending or PeyCodiStatus.Processing =>
            "Banxico está validando tu cuenta para CoDi. Puede tardar hasta 5 días hábiles; te avisaremos por correo cuando quede activa. Mientras tanto puedes preparar tus productos.",
        PeyCodiStatus.Active => "Tu cuenta puede recibir cobros con CoDi. El dinero llega directo a esta cuenta.",
        PeyCodiStatus.Error => "La cuenta no pudo registrarse para CoDi.",
        _ => "No tienes una cuenta bancaria registrada.",
    };
}
