using System.Net;
using Local133.Application.Auth;
using Local133.Application.BankAccounts;
using Local133.Application.Payments;
using Local133.Application.Tenancy;
using Local133.Domain.BankAccounts;
using Local133.Domain.Merchants;
using Local133.Domain.Pey;
using Local133.Infrastructure.Persistence;
using Local133.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Local133.IntegrationTests.Merchants;

/// <summary>Paso 2.2: alta de la cuenta bancaria (CLABE) en Pey y pantalla de estatus.</summary>
public class BankAccountTests(Local133WebFactory factory) : WebTestBase(factory)
{
    private const string Url = "/app/cuenta-bancaria";
    private const string Email = "vendedor@prueba.mx";
    private const string TestClabe = "012180015826225713";
    private const string OtherValidClabe = "032180000118359719";

    [Fact]
    public async Task Recien_registrado_ve_el_formulario()
    {
        var client = await LoggedInMerchantAsync(Email);

        var html = await GetHtmlAsync(client, Url);

        Assert.Contains("data-bank-account-form", html);
        Assert.Contains("name=\"Input.Clabe\"", html);
        Assert.Contains("data-val-clabe=", html);
        Assert.Contains("value=\"Person\"", html);
        Assert.Contains("value=\"Business\"", html);
        Assert.DoesNotContain("data-previous-error", html);
    }

    [Fact]
    public async Task Alta_valida_envia_a_Pey_guarda_la_cuenta_y_pasa_a_revision()
    {
        var client = await LoggedInMerchantAsync(Email);

        var response = await SubmitAsync(client, clabe: "012 180 015826225713", holderName: "  Ramón Orraca Lascurain ", alias: " Tacos Don Ramón ");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Url, Location(response));

        var sent = Assert.Single(Factory.Gateway.BankAccountRequests);
        Assert.Equal(new RegisterBankAccountRequest("Tacos Don Ramón", "Ramón Orraca Lascurain", TestClabe, PeyEntity.Person), sent);

        var (merchant, account) = await LoadAsync(Email);
        Assert.Equal(MerchantStatus.BankAccountInReview, merchant.Status);
        Assert.Equal("Tacos Don Ramón", merchant.BusinessName);
        Assert.NotNull(account);
        Assert.Equal(TestClabe, account.Clabe);
        Assert.Equal("Ramón Orraca Lascurain", account.HolderName);
        Assert.Equal(PeyEntity.Person, account.Entity);
        Assert.Equal(PeyCodiStatus.Pending, account.CodiStatus);
        Assert.StartsWith("01FAKE", account.PeyBankAccountId);

        var html = await GetHtmlAsync(client, Url);
        Assert.Contains("data-bank-account-status=\"Pending\"", html);
        Assert.Contains("Recibimos tu cuenta", html);
        Assert.Contains("En revisión de Banxico", html);
        Assert.Contains("•••• 5713", html);
        Assert.DoesNotContain(TestClabe, html);
        Assert.DoesNotContain("data-bank-account-form", html);
        Assert.Empty(ActivationEmails()); // en revisión: todavía no hay aviso
    }

    [Fact]
    public async Task Si_Pey_la_activa_de_inmediato_el_vendedor_queda_activo()
    {
        Factory.Gateway.RegisterStatus = PeyCodiStatus.Active;
        var client = await LoggedInMerchantAsync(Email);

        await SubmitAsync(client);

        var (merchant, account) = await LoadAsync(Email);
        Assert.Equal(MerchantStatus.Active, merchant.Status);
        Assert.Equal(PeyCodiStatus.Active, account!.CodiStatus);
        var html = await GetHtmlAsync(client, Url);
        Assert.Contains("¡Tu cuenta quedó activa!", html);
        Assert.Contains("href=\"/app/cobrar\"", html);

        var sent = Assert.Single(ActivationEmails());
        Assert.Equal(Email, sent.To);
        Assert.Equal(ActivationEmailLink, sent.Link);
        Assert.Contains("¡Hola, Tacos Don Ramón!", WebUtility.HtmlDecode(sent.Html));
    }

    [Fact]
    public async Task No_cambia_el_nombre_del_negocio_si_ya_tenia_uno()
    {
        await CreateMerchantAsync(Email);
        await UpdateMerchantAsync(Email, m => m.BusinessName = "Jugos Lupita");
        var client = CreateClient();
        await LoginAsync(client, Email);

        Assert.Contains("value=\"Jugos Lupita\"", await GetHtmlAsync(client, Url)); // precargado
        await SubmitAsync(client, alias: "Otro nombre");

        var (merchant, account) = await LoadAsync(Email);
        Assert.Equal("Jugos Lupita", merchant.BusinessName);
        Assert.Equal("Otro nombre", account!.Alias);
    }

    [Theory]
    [InlineData("012180015826225710")] // dígito verificador incorrecto
    [InlineData("01218001582622571")]  // 17 dígitos
    [InlineData("ABCDEFGHIJKLMNOPQR")]
    public async Task CLABE_invalida_no_llega_a_Pey(string clabe)
    {
        var client = await LoggedInMerchantAsync(Email);

        var response = await SubmitAsync(client, clabe: clabe);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("La CLABE debe tener 18 dígitos", html);
        Assert.Contains("data-bank-account-form", html);
        Assert.Empty(Factory.Gateway.BankAccountRequests);
        await AssertNothingSavedAsync(Email);
    }

    [Fact]
    public async Task Campos_obligatorios_vacios_muestran_errores_sin_llamar_a_Pey()
    {
        var client = await LoggedInMerchantAsync(Email);

        var response = await PostFormAsync(client, Url, new Dictionary<string, string>
        {
            ["Input.HolderName"] = "",
            ["Input.Clabe"] = "",
            ["Input.Alias"] = "",
        });

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Elige el tipo de titular.", html);
        Assert.Contains("Escribe el nombre del titular.", html);
        Assert.Contains("Escribe tu CLABE.", html);
        Assert.Contains("Escribe el nombre de tu negocio.", html);
        Assert.Empty(Factory.Gateway.BankAccountRequests);
    }

    [Fact]
    public async Task Tipo_de_titular_fuera_del_catalogo_se_rechaza()
    {
        var client = await LoggedInMerchantAsync(Email);

        var response = await SubmitAsync(client, entity: "99");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Elige el tipo de titular.", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Empty(Factory.Gateway.BankAccountRequests);
    }

    [Fact]
    public async Task Si_Pey_rechaza_la_CLABE_se_muestra_en_el_campo_y_no_se_guarda()
    {
        Factory.Gateway.RegisterException = new InvalidClabeException("INVALID_CLABE", 400, "req-1", "Invalid CLABE");
        var client = await LoggedInMerchantAsync(Email);

        var response = await SubmitAsync(client);

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("El banco no aceptó esta CLABE", html);
        Assert.Contains("data-valmsg-for=\"Input.Clabe\"", html);
        await AssertNothingSavedAsync(Email);
    }

    [Fact]
    public async Task Si_Pey_no_esta_disponible_pide_reintentar_y_no_se_guarda()
    {
        Factory.Gateway.RegisterException = new PaymentGatewayUnavailableException("timeout");
        var client = await LoggedInMerchantAsync(Email);

        var response = await SubmitAsync(client);

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Intenta de nuevo en unos minutos", html);
        Assert.Contains("data-bank-account-form", html);
        await AssertNothingSavedAsync(Email);
    }

    [Fact]
    public async Task Error_de_configuracion_muestra_mensaje_generico()
    {
        Factory.Gateway.RegisterException = new InvalidOperationException("Falta la API key de Pey");
        var client = await LoggedInMerchantAsync(Email);

        var html = WebUtility.HtmlDecode(await (await SubmitAsync(client)).Content.ReadAsStringAsync());

        Assert.Contains("problema de nuestro lado", html);
        Assert.DoesNotContain("API key", html);
        await AssertNothingSavedAsync(Email);
    }

    [Fact]
    public async Task En_revision_no_puede_enviar_otra_cuenta()
    {
        var client = await LoggedInMerchantAsync(Email);
        await SubmitAsync(client);

        // El formulario ya no existe: se arma el POST con el token de otra página del sitio.
        var response = await PostFormAsync(client, "/Identity/Account/Manage", ValidFields(OtherValidClabe), postUrl: Url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Url, Location(response));
        Assert.Single(Factory.Gateway.BankAccountRequests);
        var (_, account) = await LoadAsync(Email);
        Assert.Equal(TestClabe, account!.Clabe);
    }

    [Fact]
    public async Task Tras_un_rechazo_puede_corregirla_y_se_actualiza_la_misma_cuenta()
    {
        var client = await LoggedInMerchantAsync(Email);
        await SubmitAsync(client);
        // Lo que hará el sondeo (2.3): Pey reporta ERROR y el vendedor vuelve a PendingBankAccount.
        var (merchantBefore, rejected) = await UpdateBankAccountAsync(Email, PeyCodiStatus.Error, "Cuenta inexistente en el banco");
        Assert.Equal(MerchantStatus.PendingBankAccount, merchantBefore.Status);

        var home = await GetHtmlAsync(client, "/app");
        Assert.Contains("Tu cuenta fue rechazada: Cuenta inexistente en el banco", home);
        Assert.Contains("Corregir mi cuenta", home);

        var form = await GetHtmlAsync(client, Url);
        Assert.Contains("data-previous-error", form);
        Assert.Contains("Cuenta inexistente en el banco", form);
        Assert.Contains("Tu cuenta •••• 5713 fue rechazada", form);
        Assert.Contains("value=\"Ramón Orraca Lascurain\"", form); // datos precargados
        Assert.DoesNotContain(TestClabe, form);

        await SubmitAsync(client, clabe: OtherValidClabe, entity: nameof(PeyEntity.Business));

        Assert.Equal(2, Factory.Gateway.BankAccountRequests.Count);
        var (merchant, account) = await LoadAsync(Email);
        Assert.Equal(MerchantStatus.BankAccountInReview, merchant.Status);
        Assert.Equal(rejected.Id, account!.Id);
        Assert.Equal(OtherValidClabe, account.Clabe);
        Assert.Equal(PeyEntity.Business, account.Entity);
        Assert.Equal(PeyCodiStatus.Pending, account.CodiStatus);
        Assert.Null(account.ErrorMessage);
        Assert.NotEqual(rejected.PeyBankAccountId, account.PeyBankAccountId);
    }

    [Fact]
    public async Task Sin_token_antiforgery_responde_400()
    {
        var client = await LoggedInMerchantAsync(Email);

        var response = await client.PostAsync(Url, new FormUrlEncodedContent(ValidFields(TestClabe)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Factory.Gateway.BankAccountRequests);
    }

    [Theory]
    [InlineData(MerchantStatus.Suspended)]
    [InlineData(MerchantStatus.Disabled)]
    public async Task Suspendido_o_dado_de_baja_no_ve_el_formulario(MerchantStatus status)
    {
        await CreateMerchantAsync(Email);
        await UpdateMerchantAsync(Email, m => m.Status = status);
        var client = CreateClient();
        await LoginAsync(client, Email);

        var html = await GetHtmlAsync(client, Url);

        Assert.Contains("data-bank-account-status=\"None\"", html);
        Assert.DoesNotContain("data-bank-account-form", html);
    }

    [Fact]
    public async Task Inicio_lleva_al_formulario_y_luego_al_estatus()
    {
        var client = await LoggedInMerchantAsync(Email);

        Assert.Contains("href=\"/app/cuenta-bancaria\">Registrar mi CLABE", await GetHtmlAsync(client, "/app"));

        await SubmitAsync(client);

        var home = await GetHtmlAsync(client, "/app");
        Assert.Contains("data-merchant-status=\"BankAccountInReview\"", home);
        Assert.Contains("href=\"/app/cuenta-bancaria\">Ver estatus", home);
    }

    [Fact]
    public async Task Admin_no_entra_a_la_cuenta_bancaria_del_vendedor()
    {
        await CreateConfirmedUserAsync("admin@prueba.mx", Roles.Admin);
        var client = CreateClient();
        await LoginAsync(client, "admin@prueba.mx");

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Identity/Account/AccessDenied", Location(response));
    }

    [Fact]
    public async Task Cada_vendedor_ve_solo_su_cuenta()
    {
        var uno = await LoggedInMerchantAsync("uno@prueba.mx");
        var dos = await LoggedInMerchantAsync("dos@prueba.mx");
        await SubmitAsync(uno, holderName: "Titular Uno", alias: "Tacos Uno");

        var htmlUno = await GetHtmlAsync(uno, Url);
        var htmlDos = await GetHtmlAsync(dos, Url);

        Assert.Contains("Titular Uno", htmlUno);
        Assert.Contains("data-bank-account-form", htmlDos); // dos sigue sin cuenta
        Assert.DoesNotContain("Titular Uno", htmlDos);
        Assert.DoesNotContain("Tacos Uno", htmlDos);
    }

    [Fact]
    public async Task El_filtro_multi_tenant_de_EF_limita_las_cuentas_al_vendedor()
    {
        var uno = await LoggedInMerchantAsync("uno@prueba.mx");
        var dos = await LoggedInMerchantAsync("dos@prueba.mx");
        await SubmitAsync(uno, holderName: "Titular Uno");
        await SubmitAsync(dos, holderName: "Titular Dos", clabe: OtherValidClabe);
        var (merchantUno, _) = await LoadAsync("uno@prueba.mx");
        var (merchantDos, _) = await LoadAsync("dos@prueba.mx");

        using var scope = Factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();

        await using (var asUno = new AppDbContext(options, new FixedTenant(merchantUno.Id)))
        {
            var holder = Assert.Single(await asUno.BankAccounts.Select(b => b.HolderName).ToListAsync());
            Assert.Equal("Titular Uno", holder);
            Assert.Null(await asUno.BankAccounts.SingleOrDefaultAsync(b => b.MerchantId == merchantDos.Id));
        }

        await using (var withoutClaim = new AppDbContext(options, new FixedTenant(-1)))
        {
            Assert.Empty(await withoutClaim.BankAccounts.ToListAsync());
        }

        await using (var system = new AppDbContext(options, new SystemTenantContext()))
        {
            Assert.Equal(2, await system.BankAccounts.CountAsync());
        }
    }

    // ---- Verificar estatus (consulta manual a Pey; el sondeo de 2.3 usa el mismo IBankAccountStatusSync) ----

    private const string CheckUrl = Url + "/verificar";

    [Fact]
    public async Task En_revision_muestra_el_boton_para_verificar_el_estatus()
    {
        var client = await LoggedInMerchantAsync(Email);
        await SubmitAsync(client);

        var html = await GetHtmlAsync(client, Url);

        Assert.Contains("data-check-status", html);
        Assert.Contains("action=\"/app/cuenta-bancaria/verificar\"", html);
        Assert.Contains("Verificar estatus", html);
        Assert.Contains("Última revisión:", html);
        Assert.DoesNotContain("data-start-selling", html);
    }

    [Fact]
    public async Task Con_la_cuenta_activa_no_hay_boton_de_verificar_y_puede_comenzar_a_vender()
    {
        Factory.Gateway.RegisterStatus = PeyCodiStatus.Active;
        var client = await LoggedInMerchantAsync(Email);
        await SubmitAsync(client);

        var html = await GetHtmlAsync(client, Url);

        Assert.DoesNotContain("data-check-status", html);
        Assert.Contains("data-start-selling", html);
        Assert.Contains("Comenzar a vender", html);
    }

    [Fact]
    public async Task Verificar_cuando_Pey_ya_la_activo_deja_al_vendedor_listo_para_vender()
    {
        var client = await LoggedInMerchantAsync(Email);
        await SubmitAsync(client);
        await BackdateStatusCheckAsync(Email);
        Factory.Gateway.LookupStatus = PeyCodiStatus.Active;

        var response = await CheckStatusAsync(client);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Url, Location(response));
        var (merchant, account) = await LoadAsync(Email);
        Assert.Equal(account!.PeyBankAccountId, Assert.Single(Factory.Gateway.BankAccountLookups));
        Assert.Equal(MerchantStatus.Active, merchant.Status);
        Assert.Equal(PeyCodiStatus.Active, account.CodiStatus);

        var html = await GetHtmlAsync(client, Url);
        Assert.Contains("¡Tu cuenta ya está activa!", html);
        Assert.Contains("data-start-selling", html);
        Assert.Contains("href=\"/app/cobrar\"", html);
        Assert.DoesNotContain("data-check-status", html);
        Assert.Contains("data-merchant-status=\"Active\"", await GetHtmlAsync(client, "/app"));
        Assert.Equal(Email, Assert.Single(ActivationEmails()).To);
    }

    [Theory]
    [InlineData(PeyCodiStatus.Pending)]
    [InlineData(PeyCodiStatus.Processing)]
    public async Task Verificar_cuando_sigue_en_revision_avisa_y_registra_la_lectura(PeyCodiStatus status)
    {
        var client = await LoggedInMerchantAsync(Email);
        await SubmitAsync(client);
        var backdated = await BackdateStatusCheckAsync(Email);
        Factory.Gateway.LookupStatus = status;

        await CheckStatusAsync(client);

        var (merchant, account) = await LoadAsync(Email);
        Assert.Single(Factory.Gateway.BankAccountLookups);
        Assert.Equal(MerchantStatus.BankAccountInReview, merchant.Status);
        Assert.Equal(status, account!.CodiStatus);
        Assert.True(account.StatusCheckedAt > backdated);
        var html = await GetHtmlAsync(client, Url);
        Assert.Contains("data-status-info", html);
        Assert.Contains("Banxico sigue revisando tu cuenta", html);
        Assert.Contains("data-check-status", html);
    }

    [Fact]
    public async Task Verificar_cuando_Pey_la_rechazo_regresa_al_formulario_con_el_motivo()
    {
        var client = await LoggedInMerchantAsync(Email);
        await SubmitAsync(client);
        await BackdateStatusCheckAsync(Email);
        Factory.Gateway.LookupStatus = PeyCodiStatus.Error;
        Factory.Gateway.LookupErrorMessage = "La CLABE no pertenece al titular";

        await CheckStatusAsync(client);

        var (merchant, account) = await LoadAsync(Email);
        Assert.Equal(MerchantStatus.PendingBankAccount, merchant.Status);
        Assert.Equal(PeyCodiStatus.Error, account!.CodiStatus);
        Assert.Equal("La CLABE no pertenece al titular", account.ErrorMessage);
        var html = await GetHtmlAsync(client, Url);
        Assert.Contains("data-bank-account-form", html);
        Assert.Contains("data-previous-error", html);
        Assert.Contains("La CLABE no pertenece al titular", html);
    }

    [Fact]
    public async Task Verificar_de_nuevo_antes_de_un_minuto_no_llama_a_Pey()
    {
        var client = await LoggedInMerchantAsync(Email);
        await SubmitAsync(client); // el alta cuenta como lectura

        await CheckStatusAsync(client);

        Assert.Empty(Factory.Gateway.BankAccountLookups);
        var html = await GetHtmlAsync(client, Url);
        Assert.Contains("Acabamos de revisar tu cuenta", html);
        Assert.Contains("segundos", html);
    }

    [Fact]
    public async Task Si_Pey_no_esta_disponible_al_verificar_avisa_y_no_guarda_nada()
    {
        var client = await LoggedInMerchantAsync(Email);
        await SubmitAsync(client);
        var backdated = await BackdateStatusCheckAsync(Email);
        Factory.Gateway.LookupException = new PaymentGatewayUnavailableException("timeout");

        await CheckStatusAsync(client);

        var (merchant, account) = await LoadAsync(Email);
        Assert.Equal(MerchantStatus.BankAccountInReview, merchant.Status);
        Assert.Equal(PeyCodiStatus.Pending, account!.CodiStatus);
        Assert.Equal(backdated, account.StatusCheckedAt);
        var html = await GetHtmlAsync(client, Url);
        Assert.Contains("data-status-warning", html);
        Assert.Contains("Intenta de nuevo en unos minutos", html);
    }

    [Fact]
    public async Task Si_Pey_no_encuentra_la_cuenta_muestra_mensaje_generico()
    {
        var client = await LoggedInMerchantAsync(Email);
        await SubmitAsync(client);
        await BackdateStatusCheckAsync(Email);
        Factory.Gateway.LookupException = new PaymentGatewayNotFoundException(
            "not found", PeyErrorType.BankAccountNotFound, 404, "req-9", "Bank account not found");

        await CheckStatusAsync(client);

        var (merchant, _) = await LoadAsync(Email);
        Assert.Equal(MerchantStatus.BankAccountInReview, merchant.Status);
        var html = await GetHtmlAsync(client, Url);
        Assert.Contains("problema de nuestro lado", html);
        Assert.DoesNotContain("Bank account not found", html);
    }

    [Fact]
    public async Task Verificar_sin_token_antiforgery_responde_400()
    {
        var client = await LoggedInMerchantAsync(Email);
        await SubmitAsync(client);
        await BackdateStatusCheckAsync(Email);

        var response = await client.PostAsync(CheckUrl, new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Factory.Gateway.BankAccountLookups);
    }

    [Fact]
    public async Task Verificar_sin_cuenta_o_con_la_cuenta_activa_no_llama_a_Pey()
    {
        var sinCuenta = await LoggedInMerchantAsync("sin-cuenta@prueba.mx");
        var response = await CheckStatusAsync(sinCuenta, tokenFrom: "/Identity/Account/Manage");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Url, Location(response));

        Factory.Gateway.RegisterStatus = PeyCodiStatus.Active;
        var activo = await LoggedInMerchantAsync(Email);
        await SubmitAsync(activo);
        await BackdateStatusCheckAsync(Email);
        response = await CheckStatusAsync(activo, tokenFrom: "/Identity/Account/Manage");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        Assert.Empty(Factory.Gateway.BankAccountLookups);
    }

    [Fact]
    public async Task Suspendido_con_cuenta_en_revision_no_puede_verificar()
    {
        var client = await LoggedInMerchantAsync(Email);
        await SubmitAsync(client);
        await BackdateStatusCheckAsync(Email);
        await UpdateMerchantAsync(Email, m => m.Status = MerchantStatus.Suspended);

        Assert.DoesNotContain("data-check-status", await GetHtmlAsync(client, Url));
        await CheckStatusAsync(client, tokenFrom: "/Identity/Account/Manage");

        Assert.Empty(Factory.Gateway.BankAccountLookups);
        var (merchant, _) = await LoadAsync(Email);
        Assert.Equal(MerchantStatus.Suspended, merchant.Status);
    }

    [Fact]
    public async Task Cada_vendedor_solo_verifica_su_propia_cuenta()
    {
        var uno = await LoggedInMerchantAsync("uno@prueba.mx");
        var dos = await LoggedInMerchantAsync("dos@prueba.mx");
        await SubmitAsync(uno, holderName: "Titular Uno");
        await SubmitAsync(dos, holderName: "Titular Dos", clabe: OtherValidClabe);
        await BackdateStatusCheckAsync("uno@prueba.mx");
        await BackdateStatusCheckAsync("dos@prueba.mx");
        Factory.Gateway.LookupStatus = PeyCodiStatus.Active;

        await CheckStatusAsync(dos);

        var (merchantUno, accountUno) = await LoadAsync("uno@prueba.mx");
        var (merchantDos, accountDos) = await LoadAsync("dos@prueba.mx");
        Assert.Equal(accountDos!.PeyBankAccountId, Assert.Single(Factory.Gateway.BankAccountLookups));
        Assert.Equal(MerchantStatus.Active, merchantDos.Status);
        Assert.Equal(MerchantStatus.BankAccountInReview, merchantUno.Status);
        Assert.Equal(PeyCodiStatus.Pending, accountUno!.CodiStatus);
    }

    [Fact]
    public async Task El_caso_de_uso_se_puede_llamar_sin_sesion_como_lo_hara_el_job_de_sondeo()
    {
        var client = await LoggedInMerchantAsync(Email);
        await SubmitAsync(client);
        var (_, account) = await LoadAsync(Email);

        // Sin HttpContext ni minInterval (como el job): no aplica el freno de un minuto.
        BankAccountStatusSyncResult first, second;
        using (var scope = Factory.Services.CreateScope())
        {
            first = await scope.ServiceProvider.GetRequiredService<IBankAccountStatusSync>().SyncAsync(account!.Id);
        }

        Factory.Gateway.LookupStatus = PeyCodiStatus.Active;
        using (var scope = Factory.Services.CreateScope())
        {
            second = await scope.ServiceProvider.GetRequiredService<IBankAccountStatusSync>().SyncAsync(account.Id);
        }

        Assert.Equal(
            new BankAccountStatusSyncResult.Synced(PeyCodiStatus.Pending, PeyCodiStatus.Pending, MerchantStatus.BankAccountInReview, Changed: false),
            first);
        Assert.Equal(
            new BankAccountStatusSyncResult.Synced(PeyCodiStatus.Pending, PeyCodiStatus.Active, MerchantStatus.Active, Changed: true),
            second);
        Assert.Equal(2, Factory.Gateway.BankAccountLookups.Count);
        var (merchant, _) = await LoadAsync(Email);
        Assert.Equal(MerchantStatus.Active, merchant.Status);

        using (var scope = Factory.Services.CreateScope())
        {
            Assert.IsType<BankAccountStatusSyncResult.NotFound>(
                await scope.ServiceProvider.GetRequiredService<IBankAccountStatusSync>().SyncAsync(-1));
        }
    }

    private static Task<HttpResponseMessage> CheckStatusAsync(HttpClient client, string tokenFrom = Url) =>
        PostFormAsync(client, tokenFrom, new Dictionary<string, string>(), postUrl: CheckUrl);

    /// <summary>Mueve la última lectura dos minutos atrás para que el freno de un minuto no aplique.</summary>
    private async Task<DateTimeOffset> BackdateStatusCheckAsync(string email)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        var merchantId = await db.Merchants.Where(m => m.UserId == userId).Select(m => m.Id).SingleAsync();
        var account = await db.BankAccounts.SingleAsync(b => b.MerchantId == merchantId);
        account.StatusCheckedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        await db.SaveChangesAsync();
        return account.StatusCheckedAt.Value;
    }

    private async Task<HttpClient> LoggedInMerchantAsync(string email)
    {
        await CreateMerchantAsync(email);
        var client = CreateClient();
        Assert.Equal("/app", Location(await LoginAsync(client, email)));
        return client;
    }

    private static Dictionary<string, string> ValidFields(
        string clabe, string holderName = "Ramón Orraca Lascurain", string alias = "Tacos Don Ramón", string entity = nameof(PeyEntity.Person)) => new()
    {
        ["Input.Entity"] = entity,
        ["Input.HolderName"] = holderName,
        ["Input.Clabe"] = clabe,
        ["Input.Alias"] = alias,
    };

    private static Task<HttpResponseMessage> SubmitAsync(
        HttpClient client,
        string clabe = TestClabe,
        string holderName = "Ramón Orraca Lascurain",
        string alias = "Tacos Don Ramón",
        string entity = nameof(PeyEntity.Person)) =>
        PostFormAsync(client, Url, ValidFields(clabe, holderName, alias, entity));

    private static async Task<string> GetHtmlAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private async Task<(Merchant Merchant, BankAccount? Account)> LoadAsync(string email)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        var merchant = await db.Merchants.AsNoTracking().SingleAsync(m => m.UserId == userId);
        var account = await db.BankAccounts.AsNoTracking().SingleOrDefaultAsync(b => b.MerchantId == merchant.Id);
        return (merchant, account);
    }

    private async Task AssertNothingSavedAsync(string email)
    {
        var (merchant, account) = await LoadAsync(email);
        Assert.Equal(MerchantStatus.PendingBankAccount, merchant.Status);
        Assert.Null(account);
    }

    private async Task UpdateMerchantAsync(string email, Action<Merchant> change)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        change(await db.Merchants.SingleAsync(m => m.UserId == userId));
        await db.SaveChangesAsync();
    }

    private async Task<(Merchant Merchant, BankAccount Account)> UpdateBankAccountAsync(string email, PeyCodiStatus status, string? error)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        var merchant = await db.Merchants.SingleAsync(m => m.UserId == userId);
        var account = await db.BankAccounts.SingleAsync(b => b.MerchantId == merchant.Id);
        account.UpdateStatus(status, error, DateTimeOffset.UtcNow);
        merchant.ApplyBankAccountStatus(status);
        await db.SaveChangesAsync();
        return (merchant, account);
    }

    private sealed class FixedTenant(int merchantId) : ITenantContext
    {
        public bool IsMerchantScoped => true;

        public int? MerchantId => merchantId;
    }
}
