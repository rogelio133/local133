using Local133.Application.Tenancy;
using Local133.Domain.BankAccounts;
using Local133.Domain.Merchants;
using Local133.Domain.Payments;
using Local133.Domain.Sales;
using Local133.Domain.Settings;
using Local133.Domain.Webhooks;
using Local133.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Local133.Infrastructure.Persistence;

/// <remarks>
/// Multi-tenant: las entidades de negocio (con <c>MerchantId</c>) llevan un filtro global que usa
/// <see cref="IsMerchantScoped"/> / <see cref="CurrentMerchantId"/>; EF Core los evalúa por instancia en cada
/// consulta. <see cref="Merchants"/> no se filtra (es el tenant).
/// </remarks>
public class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext? tenant = null)
    : IdentityDbContext<ApplicationUser>(options)
{
    private readonly ITenantContext _tenant = tenant ?? new SystemTenantContext();

    public DbSet<SystemSettings> SystemSettings => Set<SystemSettings>();

    public DbSet<Merchant> Merchants => Set<Merchant>();

    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();

    public DbSet<Sale> Sales => Set<Sale>();

    public DbSet<Payment> Payments => Set<Payment>();

    /// <summary>Bitácora de webhooks de Pey (sin filtro: no es de un vendedor).</summary>
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();

    /// <summary>La petición es de un vendedor: las entidades de negocio se filtran por <see cref="CurrentMerchantId"/>.</summary>
    public bool IsMerchantScoped => _tenant.IsMerchantScoped;

    public int? CurrentMerchantId => _tenant.MerchantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Primero el modelo de Identity (AspNet*), luego las configuraciones propias.
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Filtros multi-tenant (referencian propiedades del contexto para que se parametricen por instancia).
        modelBuilder.Entity<BankAccount>().HasQueryFilter(b => !IsMerchantScoped || b.MerchantId == CurrentMerchantId);
        modelBuilder.Entity<Sale>().HasQueryFilter(s => !IsMerchantScoped || s.MerchantId == CurrentMerchantId);
        modelBuilder.Entity<Payment>().HasQueryFilter(p => !IsMerchantScoped || p.MerchantId == CurrentMerchantId);
    }
}
