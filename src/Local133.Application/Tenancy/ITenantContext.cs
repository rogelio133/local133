namespace Local133.Application.Tenancy;

/// <summary>
/// Vendedor (tenant) de la petición actual. Con <see cref="IsMerchantScoped"/> el <c>AppDbContext</c> filtra toda
/// entidad de negocio por <see cref="MerchantId"/>. Sin alcance (admin, webhook, jobs, herramientas) ve todo.
/// </summary>
public interface ITenantContext
{
    /// <summary>La petición es de un vendedor: solo debe ver sus propios datos.</summary>
    bool IsMerchantScoped { get; }

    /// <summary>Id del vendedor cuando <see cref="IsMerchantScoped"/>; si no se pudo resolver, un id que no existe.</summary>
    int? MerchantId { get; }
}

/// <summary>Sin alcance: jobs, webhook, migraciones y pruebas de infraestructura.</summary>
public sealed class SystemTenantContext : ITenantContext
{
    public bool IsMerchantScoped => false;

    public int? MerchantId => null;
}
