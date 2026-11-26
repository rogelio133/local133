using System.Net;
using System.Text.RegularExpressions;
using Local133.Application.Payments;
using Local133.Application.Sales;
using Local133.Domain.BankAccounts;
using Local133.Domain.Merchants;
using Local133.Domain.Pey;
using Local133.Domain.Sales;
using Local133.Infrastructure.Persistence;
using Local133.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Local133.IntegrationTests.Sales;

/// <summary>Paso 2.4: venta por monto directo con QR CoDi dinámico (QR dibujado en cliente con QRCode.js).</summary>
public partial class DirectSaleTests(Local133WebFactory factory) : WebTestBase(factory)
{
    private const string Url = "/app/cobrar";
    private const string Email = "vendedor@prueba.mx";
    private const string OtherEmail = "otro@prueba.mx";
    private const string TestClabe = "012180015826225713";

    [Fact]
    public async Task Vendedor_activo_ve_el_formulario_con_un_centavo_por_defecto()
    {
        var client = await ActiveMerchantAsync(Email);

        var html = await GetHtmlAsync(client, Url);

        Assert.Contains("data-charge-form", html);
        Assert.Contains("name=\"Input.Amount\"", html);
        Assert.Contains("value=\"0.01\"", html);
        Assert.Contains("inputmode=\"decimal\"", html);
        Assert.Contains("data-amount-input", html);
        Assert.Contains($"data-val-regex-pattern=\"{ChargeInputPattern}\"", html);
        Assert.Contains("/js/amount-input", html);
        Assert.Contains("name=\"Input.Concept\"", html);
        Assert.Contains("data-concept-input data-val-trim", html);
        Assert.Contains("data-val-required=\"Escribe el concepto del cobro.\"", html);
        Assert.Contains($"data-val-regex-pattern=\"{Sale.ConceptTextPattern}\"", html);
        Assert.Contains($"maxlength=\"{Sale.ConceptTextMaxLength}\"", html);
        Assert.Contains("aria-current=\"page\"", html);
        Assert.Matches(new Regex("class=\"nav-link active\" href=\"/app/cobrar\""), html); // pestaña "Cobrar"
        Assert.DoesNotContain("data-charge-blocked", html);
    }

    [Theory]
    [InlineData(MerchantStatus.PendingBankAccount, "Registra tu cuenta bancaria")]
    [InlineData(MerchantStatus.BankAccountInReview, "Tu cuenta está en revisión")]
    [InlineData(MerchantStatus.Suspended, "Tu cuenta está suspendida")]
    [InlineData(MerchantStatus.Disabled, "Tu cuenta está dada de baja")]
    public async Task Vendedor_que_no_puede_cobrar_ve_el_aviso_y_su_POST_no_llama_a_Pey(MerchantStatus status, string notice)
    {
        await CreateMerchantAsync(Email);
        await UpdateMerchantAsync(Email, m => m.Status = status);
        var client = CreateClient();
        await LoginAsync(client, Email);

        var html = await GetHtmlAsync(client, Url);
        Assert.Contains("data-charge-blocked", html);
        Assert.Contains(notice, html);
        Assert.DoesNotContain("data-charge-form", html);

        // POST directo (con un token válido de otra página del sitio).
        var response = await PostFormAsync(client, TokenPage, ChargeFields("10"), postUrl: Url);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Url, Location(response));
        Assert.Empty(Factory.Gateway.CodiQrRequests);
        Assert.Empty(await LoadSalesAsync(Email));
    }

    [Fact]
    public async Task Activo_con_la_cuenta_bancaria_no_activa_no_puede_cobrar()
    {
        var client = await ActiveMerchantAsync(Email, PeyCodiStatus.Error);

        var html = await GetHtmlAsync(client, Url);

        Assert.Contains("Tu cuenta bancaria no está activa", html);
        var response = await PostFormAsync(client, TokenPage, ChargeFields("10"), postUrl: Url);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Empty(Factory.Gateway.CodiQrRequests);
    }

    [Fact]
    public async Task Cobro_valido_pide_el_QR_a_Pey_guarda_la_venta_y_muestra_el_QR()
    {
        var client = await ActiveMerchantAsync(Email, businessName: "Tacos Don Ramón");

        var response = await ChargeAsync(client, "150.50");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var sale = Assert.Single(await LoadSalesAsync(Email));
        Assert.Equal($"{Url}/venta/{sale.Id}", Location(response));

        var merchantId = sale.MerchantId;
        var sent = Assert.Single(Factory.Gateway.CodiQrRequests);
        Assert.Equal(150.50m, sent.Amount);
        Assert.Equal(600, sent.ExpirationSeconds);
        Assert.Equal(1, sent.NumRef);
        Assert.Equal("2 tacos al pastor", sent.Concept); // sin el nombre del negocio (ajustes 2026-11-26)
        Assert.Equal(await PeyBankAccountIdAsync(Email), sent.BankAccountId);
        Assert.NotNull(sent.Metadata);
        Assert.Equal(SaleMetadata.SaleKind, sent.Metadata[SaleMetadata.KindKey]);
        Assert.Equal(merchantId.ToString(), sent.Metadata[SaleMetadata.MerchantIdKey]);
        Assert.Equal(sale.Id.ToString(), sent.Metadata[SaleMetadata.SaleIdKey]);

        Assert.Equal(1, sale.Folio);
        Assert.Equal(150.50m, sale.Amount);
        Assert.Equal("2 tacos al pastor", sale.Concept);
        Assert.Equal(SaleStatus.Pending, sale.Status);
        Assert.Equal(ChargeMethod.DynamicQr, sale.Method);
        Assert.Equal(SaleSource.DirectAmount, sale.Source);
        Assert.StartsWith("IDCFAKE", sale.PeyQrId);
        Assert.Equal(FakePaymentGateway.QrStringFor(sale.PeyQrId!), sale.QrString);
        Assert.NotNull(sale.ExpiresAt);
        Assert.InRange((sale.ExpiresAt.Value - sale.CreatedAt).TotalSeconds, 599.9, 600.1);

        var html = await GetHtmlAsync(client, Location(response)!);
        Assert.Contains("data-sale-status=\"Pending\"", html);
        Assert.Contains("data-expired=\"false\"", html);
        Assert.Contains("Venta 1", html);
        Assert.Contains("$150.50", html);
        Assert.Contains("data-sale-concept>2 tacos al pastor<", html);
        Assert.Contains($"data-qr-string=\"{sale.QrString}\"", html);
        Assert.Matches(ExpiresInRegex(), html);
        Assert.Contains("/lib/qrcodejs/qrcode.min", html);
        Assert.Contains("/js/sale-qr", html);
        Assert.Matches(new Regex("class=\"nav-link active\" href=\"/app/cobrar\""), html);
    }

    [Fact]
    public async Task Un_centavo_es_el_minimo_aceptado()
    {
        var client = await ActiveMerchantAsync(Email);

        var response = await ChargeAsync(client, "0.01");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(0.01m, Assert.Single(Factory.Gateway.CodiQrRequests).Amount);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("-0.01")]
    [InlineData("1.005")]
    [InlineData("0")]
    [InlineData("0.00")]
    [InlineData("abc")]
    [InlineData("1,000")]
    [InlineData("1e3")]
    [InlineData("1000000")]
    [InlineData("")]
    public async Task Monto_invalido_no_llega_a_Pey(string amount)
    {
        var client = await ActiveMerchantAsync(Email);

        var response = await ChargeAsync(client, amount);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("data-charge-form", html);
        Assert.Matches(new Regex("field-validation-error[^>]*>[^<]+<"), html);
        Assert.Empty(Factory.Gateway.CodiQrRequests);
        Assert.Empty(await LoadSalesAsync(Email));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Tacos Don Ramón")]
    public async Task El_concepto_es_solo_el_texto_capturado_con_o_sin_nombre_de_negocio(string? businessName)
    {
        var client = await ActiveMerchantAsync(Email, businessName: businessName);

        await ChargeAsync(client, "10", "  Café con pan  ");

        Assert.Equal("Café con pan", Assert.Single(Factory.Gateway.CodiQrRequests).Concept);
        Assert.Equal("Café con pan", Assert.Single(await LoadSalesAsync(Email)).Concept);
    }

    [Fact]
    public async Task El_formulario_precarga_el_concepto_por_defecto_de_mi_informacion()
    {
        var client = await ActiveMerchantAsync(Email);
        await UpdateMerchantAsync(Email, m => m.DefaultConcept = "Consumo");

        var html = await GetHtmlAsync(client, Url);

        Assert.Matches(new Regex("<input(?=[^>]*name=\"Input.Concept\")(?=[^>]*value=\"Consumo\")[^>]*>"), html);
        Assert.Contains("Tu cliente lo verá en su app bancaria.", html);
        Assert.DoesNotContain("junto con el nombre de tu negocio", html);
    }

    [Fact]
    public async Task Sin_concepto_por_defecto_el_campo_va_vacio()
    {
        var client = await ActiveMerchantAsync(Email);

        var html = await GetHtmlAsync(client, Url);

        Assert.DoesNotMatch(new Regex("<input(?=[^>]*name=\"Input.Concept\")(?=[^>]*value=\"[^\"]+\")[^>]*>"), html);
    }

    [Fact]
    public async Task Tras_un_error_se_conserva_lo_capturado_y_no_el_concepto_por_defecto()
    {
        var client = await ActiveMerchantAsync(Email);
        await UpdateMerchantAsync(Email, m => m.DefaultConcept = "Consumo");

        var response = await ChargeAsync(client, "abc", "Refresco");

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("value=\"Refresco\"", html);
        Assert.DoesNotContain("value=\"Consumo\"", html);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("🌮")]
    [InlineData("<b>hola</b>")]
    [InlineData("correo@prueba.mx")]
    public async Task Concepto_invalido_no_llega_a_Pey(string concept)
    {
        var client = await ActiveMerchantAsync(Email);

        var response = await ChargeAsync(client, "10", concept);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("data-charge-form", html);
        Assert.Matches(new Regex("<span(?=[^>]*field-validation-error)(?=[^>]*data-valmsg-for=\"Input.Concept\")[^>]*>[^<]+<"), html);
        Assert.Contains("value=\"10\"", html); // el monto capturado se conserva
        Assert.Empty(Factory.Gateway.CodiQrRequests);
        Assert.Empty(await LoadSalesAsync(Email));
    }

    [Fact]
    public async Task Concepto_de_mas_de_100_caracteres_no_llega_a_Pey()
    {
        var client = await ActiveMerchantAsync(Email);

        var response = await ChargeAsync(client, "10", new string('a', Sale.ConceptTextMaxLength + 1));

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("El concepto admite hasta 100 caracteres.", html);
        Assert.Empty(Factory.Gateway.CodiQrRequests);
    }

    [Fact]
    public async Task Con_monto_invalido_se_conserva_el_concepto_capturado()
    {
        var client = await ActiveMerchantAsync(Email);

        var response = await ChargeAsync(client, "abc", "Pedido #12 (para llevar)");

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("value=\"Pedido #12 (para llevar)\"", html);
        Assert.Empty(Factory.Gateway.CodiQrRequests);
    }

    [Fact]
    public async Task Pey_no_disponible_muestra_el_aviso_y_no_guarda_la_venta()
    {
        Factory.Gateway.CodiQrException = new PaymentGatewayUnavailableException("sin respuesta");
        var client = await ActiveMerchantAsync(Email);

        var response = await ChargeAsync(client, "10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("No pudimos comunicarnos con el servicio de pagos", html);
        Assert.Contains("value=\"10\"", html);
        Assert.Single(Factory.Gateway.CodiQrRequests);
        Assert.Empty(await LoadSalesAsync(Email));
    }

    [Fact]
    public async Task Pey_rechaza_el_cobro_muestra_el_aviso_y_no_guarda_la_venta()
    {
        Factory.Gateway.CodiQrException = new PaymentGatewayValidationException(
            "monto inválido", PeyErrorType.InvalidAmount, 400, "req-1", "Invalid amount");
        var client = await ActiveMerchantAsync(Email);

        var response = await ChargeAsync(client, "10");

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("El servicio de pagos no aceptó el cobro", html);
        Assert.Empty(await LoadSalesAsync(Email));
    }

    [Fact]
    public async Task Tras_una_falla_el_folio_no_se_salta()
    {
        var client = await ActiveMerchantAsync(Email);
        Factory.Gateway.CodiQrException = new PaymentGatewayUnavailableException("sin respuesta");
        await ChargeAsync(client, "10");
        Factory.Gateway.CodiQrException = null;

        await ChargeAsync(client, "10");

        Assert.Equal(1, Assert.Single(await LoadSalesAsync(Email)).Folio);
    }

    [Fact]
    public async Task Los_folios_son_consecutivos_por_vendedor()
    {
        var client = await ActiveMerchantAsync(Email);
        var other = await ActiveMerchantAsync(OtherEmail);

        await ChargeAsync(client, "1");
        await ChargeAsync(client, "2");
        await ChargeAsync(other, "3");
        await ChargeAsync(client, "4");

        Assert.Equal([1, 2, 3], (await LoadSalesAsync(Email)).Select(s => s.Folio));
        Assert.Equal([1], (await LoadSalesAsync(OtherEmail)).Select(s => s.Folio));
        Assert.Equal([1, 2, 1, 3], Factory.Gateway.CodiQrRequests.Select(r => r.NumRef!.Value));
    }

    [Fact]
    public async Task Un_vendedor_no_ve_la_venta_de_otro()
    {
        var client = await ActiveMerchantAsync(Email);
        var other = await ActiveMerchantAsync(OtherEmail);
        var location = Location(await ChargeAsync(client, "10"))!;

        var response = await other.GetAsync(location);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(location)).StatusCode);
    }

    [Fact]
    public async Task Venta_inexistente_da_404()
    {
        var client = await ActiveMerchantAsync(Email);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Url}/venta/999999")).StatusCode);
    }

    [Fact]
    public async Task QR_vencido_se_muestra_como_expirado_sin_QR()
    {
        var client = await ActiveMerchantAsync(Email);
        var location = Location(await ChargeAsync(client, "10"))!;
        await UpdateSalesAsync(Email, s => s.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1));

        var html = await GetHtmlAsync(client, location);

        Assert.Contains("data-expired=\"true\"", html);
        Assert.Contains("Este QR expiró sin pago", html);
        Assert.DoesNotContain("data-qr-string", html);
        Assert.DoesNotContain("qrcode.min", html);
    }

    [Fact]
    public async Task Sin_token_antiforgery_responde_400()
    {
        var client = await ActiveMerchantAsync(Email);

        var response = await client.PostAsync(Url, new FormUrlEncodedContent(ChargeFields("10")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Factory.Gateway.CodiQrRequests);
    }

    [Fact]
    public async Task Admin_no_entra_a_cobrar()
    {
        await CreateConfirmedUserAsync("admin@prueba.mx", Application.Auth.Roles.Admin);
        var client = CreateClient();
        await LoginAsync(client, "admin@prueba.mx");

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Identity/Account/AccessDenied", Location(response));
    }

    // GetHtmlAsync decodifica el HTML: el patrón aparece literal.
    private static string ChargeInputPattern => Web.Areas.Merchant.Models.ChargeInput.AmountPattern;

    /// <summary>Página con un formulario (token antiforgery válido) para hacer POST directos aunque Cobrar esté bloqueado.</summary>
    private const string TokenPage = "/Identity/Account/Manage";

    private const string DefaultConcept = "2 tacos al pastor";

    private static Dictionary<string, string> ChargeFields(string amount, string concept = DefaultConcept) =>
        new() { ["Input.Amount"] = amount, ["Input.Concept"] = concept };

    private static Task<HttpResponseMessage> ChargeAsync(HttpClient client, string amount, string concept = DefaultConcept) =>
        PostFormAsync(client, Url, ChargeFields(amount, concept));

    private static async Task<string> GetHtmlAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Vendedor <c>Active</c> con su cuenta bancaria en el estatus indicado, con sesión iniciada.</summary>
    private async Task<HttpClient> ActiveMerchantAsync(
        string email, PeyCodiStatus codiStatus = PeyCodiStatus.Active, string? businessName = null)
    {
        await CreateMerchantAsync(email);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var merchant = await MerchantOfAsync(db, email);
            merchant.Status = MerchantStatus.Active;
            merchant.BusinessName = businessName;
            db.BankAccounts.Add(BankAccount.Submit(
                merchant.Id, "Ramón Orraca Lascurain", TestClabe, "Puesto", PeyEntity.Person,
                $"01PEY{merchant.Id:D10}", codiStatus, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var client = CreateClient();
        Assert.Equal("/app", Location(await LoginAsync(client, email)));
        return client;
    }

    private async Task UpdateMerchantAsync(string email, Action<Merchant> change)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        change(await MerchantOfAsync(db, email));
        await db.SaveChangesAsync();
    }

    private async Task<string> PeyBankAccountIdAsync(string email)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var merchant = await MerchantOfAsync(db, email);
        return await db.BankAccounts.Where(b => b.MerchantId == merchant.Id).Select(b => b.PeyBankAccountId).SingleAsync();
    }

    private async Task<List<Sale>> LoadSalesAsync(string email)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var merchant = await MerchantOfAsync(db, email);
        return await db.Sales.AsNoTracking().Where(s => s.MerchantId == merchant.Id).OrderBy(s => s.Folio).ToListAsync();
    }

    private async Task UpdateSalesAsync(string email, Action<Sale> change)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var merchant = await MerchantOfAsync(db, email);
        foreach (var sale in await db.Sales.Where(s => s.MerchantId == merchant.Id).ToListAsync())
        {
            change(sale);
        }

        await db.SaveChangesAsync();
    }

    private static async Task<Merchant> MerchantOfAsync(AppDbContext db, string email)
    {
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        return await db.Merchants.SingleAsync(m => m.UserId == userId);
    }

    [GeneratedRegex("data-expires-in=\"(59\\d|600)\"")]
    private static partial Regex ExpiresInRegex();
}
