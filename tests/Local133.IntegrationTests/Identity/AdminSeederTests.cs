using Local133.Application.Auth;
using Local133.Infrastructure.Identity;
using Local133.IntegrationTests.Infrastructure;
using Local133.Web.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Local133.IntegrationTests.Identity;

/// <summary>Paso 1.5: siembra del admin desde Seed:Admin:Email/Password.</summary>
public class AdminSeederTests(Local133WebFactory factory) : WebTestBase(factory)
{
    private const string AdminEmail = "admin@prueba.mx";

    [Fact]
    public async Task Crea_el_admin_con_correo_confirmado_y_entra_a_admin()
    {
        Assert.Equal(AdminSeedResult.Created, await SeedAsync(AdminEmail, Password));

        await AssertAdminAsync(AdminEmail);
        var client = CreateClient();
        Assert.Equal("/admin/clientes", Location(await LoginAsync(client, AdminEmail)));
    }

    [Fact]
    public async Task Es_idempotente_y_no_cambia_la_contrasena()
    {
        await SeedAsync(AdminEmail, Password);

        Assert.Equal(AdminSeedResult.AlreadyAdmin, await SeedAsync(AdminEmail, "OtraClave999"));

        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Single(users.Users, u => u.Email == AdminEmail);
        Assert.Equal("/admin/clientes", Location(await LoginAsync(CreateClient(), AdminEmail, Password)));
    }

    [Fact]
    public async Task No_convierte_en_admin_a_un_vendedor()
    {
        await CreateMerchantAsync("vendedor@prueba.mx");

        Assert.Equal(AdminSeedResult.RejectedMerchant, await SeedAsync("vendedor@prueba.mx", Password));

        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync("vendedor@prueba.mx");
        Assert.False(await users.IsInRoleAsync(user!, Roles.Admin));
    }

    [Fact]
    public async Task Contrasena_fuera_de_politica_no_crea_nada()
    {
        Assert.Equal(AdminSeedResult.Failed, await SeedAsync(AdminEmail, "corta"));

        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Null(await users.FindByEmailAsync(AdminEmail));
    }

    [Fact]
    public async Task Usuario_sin_roles_recibe_el_rol_admin()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var created = await users.CreateAsync(new ApplicationUser { UserName = AdminEmail, Email = AdminEmail, EmailConfirmed = true }, Password);
            Assert.True(created.Succeeded);
        }

        Assert.Equal(AdminSeedResult.Promoted, await SeedAsync(AdminEmail, "OtraClave999"));

        await AssertAdminAsync(AdminEmail);
    }

    [Fact]
    public async Task Al_arrancar_con_la_configuracion_se_crea_el_admin()
    {
        await using var seeded = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [AdminSeeder.EmailKey] = AdminEmail,
                [AdminSeeder.PasswordKey] = Password,
            })));

        // Crear el cliente arranca el host, que siembra antes de atender.
        var client = seeded.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

        await AssertAdminAsync(AdminEmail);
        Assert.Equal("/admin/clientes", Location(await LoginAsync(client, AdminEmail)));
    }

    private async Task<AdminSeedResult> SeedAsync(string email, string password)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AdminSeeder>().SeedAsync(email, password);
    }

    private async Task AssertAdminAsync(string email)
    {
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.True(user.EmailConfirmed);
        Assert.Equal([Roles.Admin], await users.GetRolesAsync(user));
    }
}
