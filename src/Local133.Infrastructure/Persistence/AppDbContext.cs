using Local133.Application.Tenancy;
using Local133.Domain.Audit;
using Local133.Domain.BankAccounts;
using Local133.Domain.Merchants;
using Local133.Domain.Notifications;
using Local133.Domain.Payments;
using Local133.Domain.Products;
using Local133.Domain.Sales;
using Local133.Domain.Settings;
using Local133.Domain.Subscriptions;
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

    /// <summary>Productos de las ventas con carrito (copia del nombre y el precio).</summary>
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();

    public DbSet<Payment> Payments => Set<Payment>();

    /// <summary>Catálogo del vendedor; la baja lógica no va en el filtro global (consultar con <c>!IsDeleted</c>).</summary>
    public DbSet<Product> Products => Set<Product>();

    /// <summary>Suscripción del vendedor (una por vendedor; nace con la prueba).</summary>
    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    /// <summary>Días de Pro del vendedor (prueba, pagados, premios y regalos).</summary>
    public DbSet<SubscriptionPeriod> SubscriptionPeriods => Set<SubscriptionPeriod>();

    /// <summary>Cargos de la suscripción (un link de pago de Pey por cargo).</summary>
    public DbSet<SubscriptionCharge> SubscriptionCharges => Set<SubscriptionCharge>();

    /// <summary>Transacciones que Pey reportó en los links de la suscripción.</summary>
    public DbSet<SubscriptionPayment> SubscriptionPayments => Set<SubscriptionPayment>();

    /// <summary>Vendedores que se registraron con el código de otro (5.8).</summary>
    public DbSet<Referral> Referrals => Set<Referral>();

    /// <summary>Avisos al vendedor en el sitio (únicos por vendedor y <c>DedupKey</c>).</summary>
    public DbSet<Notification> Notifications => Set<Notification>();

    /// <summary>Bitácora de cambios del admin (6.2; sin filtro: no la lee ningún vendedor).</summary>
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

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
        modelBuilder.Entity<SaleItem>().HasQueryFilter(i => !IsMerchantScoped || i.MerchantId == CurrentMerchantId);
        modelBuilder.Entity<Payment>().HasQueryFilter(p => !IsMerchantScoped || p.MerchantId == CurrentMerchantId);
        modelBuilder.Entity<Product>().HasQueryFilter(p => !IsMerchantScoped || p.MerchantId == CurrentMerchantId);
        modelBuilder.Entity<Subscription>().HasQueryFilter(s => !IsMerchantScoped || s.MerchantId == CurrentMerchantId);
        modelBuilder.Entity<SubscriptionPeriod>().HasQueryFilter(p => !IsMerchantScoped || p.MerchantId == CurrentMerchantId);
        modelBuilder.Entity<SubscriptionCharge>().HasQueryFilter(c => !IsMerchantScoped || c.MerchantId == CurrentMerchantId);
        modelBuilder.Entity<SubscriptionPayment>().HasQueryFilter(p => !IsMerchantScoped || p.MerchantId == CurrentMerchantId);
        // Un referido lo ven los dos vendedores: quien invitó («Invita y gana») y el referido (su descuento).
        modelBuilder.Entity<Referral>().HasQueryFilter(r =>
            !IsMerchantScoped || r.ReferrerMerchantId == CurrentMerchantId || r.ReferredMerchantId == CurrentMerchantId);
        modelBuilder.Entity<Notification>().HasQueryFilter(n => !IsMerchantScoped || n.MerchantId == CurrentMerchantId);
    }
}
