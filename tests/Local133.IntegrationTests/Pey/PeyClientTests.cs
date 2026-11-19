using System.Text.Json;

using Local133.Application.Payments;
using Local133.Domain.Pey;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Local133.IntegrationTests.Pey;

/// <summary>Contratos de <c>PeyClient</c> contra Pey simulado (cuerpos tomados del OpenAPI 2026-10-20).</summary>
public sealed class PeyClientTests : IDisposable
{
    private const string Account = "01JBAV";

    private readonly PeyFixture _pey = new();

    public void Dispose() => _pey.Dispose();

    private void Respond(string method, string path, int status, string body) =>
        _pey.Server
            .Given(Request.Create().WithPath(path).UsingMethod(method))
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body));

    [Fact]
    public async Task RegisterBankAccount_EnviaCuerpoYApiKey_YLeeEstatus()
    {
        Respond("POST", "/bank-account", 200, """{"id":"01JBAVWE4NS5TGXHWGMX3KH23Q","codiStatus":"PENDING"}""");

        var result = await _pey.Gateway.RegisterBankAccountAsync(
            new RegisterBankAccountRequest(" Tacos Pey ", "Ramón Orraca Lascurain", "012180015826225713", PeyEntity.Person));

        Assert.Equal(new RegisteredBankAccount("01JBAVWE4NS5TGXHWGMX3KH23Q", PeyCodiStatus.Pending), result);
        var request = _pey.SingleRequest();
        Assert.Equal($"Bearer {PeyFixture.ApiKey}", request.Headers!["Authorization"].Single());
        var body = PeyFixture.BodyOf(request);
        Assert.Equal("Tacos Pey", body.GetProperty("alias").GetString());
        Assert.Equal("Ramón Orraca Lascurain", body.GetProperty("name").GetString());
        Assert.Equal("012180015826225713", body.GetProperty("clabe").GetString());
        Assert.Equal("PERSON", body.GetProperty("entity").GetString());
    }

    [Fact]
    public async Task GetBankAccount_LeeErrorYFecha()
    {
        Respond("GET", "/bank-account/01JBAV", 200, """
            {"id":"01JBAV","alias":"BBVA","name":"Pagos con Pey S.A. de C.V.","clabe":"012180001234567899",
             "entity":"BUSINESS","codiStatus":"ERROR","errorMessage":"Cuenta no habilitada para CoDi","dateCreated":1760000000}
            """);

        var account = await _pey.Gateway.GetBankAccountAsync("01JBAV");

        Assert.Equal(PeyCodiStatus.Error, account.CodiStatus);
        Assert.Equal("Cuenta no habilitada para CoDi", account.ErrorMessage);
        Assert.Equal(PeyEntity.Business, account.Entity);
        Assert.Null(account.TaxId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1760000000), account.CreatedAt);
    }

    [Fact]
    public async Task CreateCodiQr_EnviaMetadataYNumRefComoTexto()
    {
        Respond("POST", "/codi/qr", 201, """{"id":"AB12CD34EF","qrString":"{\"TYP\":1,\"IDC\":\"AB12CD34EF\"}"}""");

        var qr = await _pey.Gateway.CreateCodiQrAsync(new CreateCodiQrRequest(
            Amount: 150.50m,
            ExpirationSeconds: 600,
            Concept: "Venta 123",
            NumRef: 123,
            BankAccountId: "01JBAV",
            Metadata: new Dictionary<string, string> { ["kind"] = "sale", ["merchantId"] = "7", ["saleId"] = "123" }));

        Assert.Equal("AB12CD34EF", qr.Id);
        Assert.Equal("""{"TYP":1,"IDC":"AB12CD34EF"}""", qr.QrString);
        var body = PeyFixture.BodyOf(_pey.SingleRequest());
        Assert.Equal(150.50m, body.GetProperty("amount").GetDecimal());
        Assert.Equal(600, body.GetProperty("expiration").GetInt32());
        Assert.Equal("123", body.GetProperty("numRef").GetString());
        Assert.Equal("01JBAV", body.GetProperty("bankAccountId").GetString());
        Assert.Equal("sale", body.GetProperty("metadata").GetProperty("kind").GetString());
        Assert.False(body.GetProperty("generateImage").GetBoolean()); // siempre explícito: el QR se dibuja en cliente
    }

    [Fact]
    public async Task CreateCodiQr_MontoAbierto_OmiteCamposOpcionales()
    {
        Respond("POST", "/codi/qr", 201, """{"id":"IDC1","qrString":"x"}""");

        await _pey.Gateway.CreateCodiQrAsync(new CreateCodiQrRequest(Amount: 0, ExpirationSeconds: PeyLimits.NoExpiration, BankAccountId: Account));

        var body = PeyFixture.BodyOf(_pey.SingleRequest());
        Assert.Equal(0, body.GetProperty("amount").GetDecimal());
        Assert.Equal(0, body.GetProperty("expiration").GetInt32());
        Assert.Equal(Account, body.GetProperty("bankAccountId").GetString()); // siempre a la cuenta del vendedor
        Assert.False(body.TryGetProperty("numRef", out _));
        Assert.False(body.TryGetProperty("metadata", out _));
        Assert.False(body.TryGetProperty("concept", out _));
        Assert.False(body.GetProperty("generateImage").GetBoolean());
    }

    [Fact]
    public async Task CreateCodiPush_EnviaCelularYLeeTransaccion()
    {
        Respond("POST", "/codi/pn", 200, """{"transactionId":"01J5KXYZ1234567890ABCD","codiId":"12345678901234567890"}""");

        var push = await _pey.Gateway.CreateCodiPushNotificationAsync(
            new CreateCodiPushNotificationRequest(0.01m, "5512345678", Account, Concept: "Prueba"));

        Assert.Equal(new CodiPushNotification("01J5KXYZ1234567890ABCD", "12345678901234567890"), push);
        var body = PeyFixture.BodyOf(_pey.SingleRequest());
        Assert.Equal("5512345678", body.GetProperty("phoneNumber").GetString());
        Assert.Equal(0.01m, body.GetProperty("amount").GetDecimal());
        Assert.Equal(PeyLimits.MinExpirationSeconds, body.GetProperty("expiration").GetInt32());
    }

    [Fact]
    public async Task CreatePaymentLink_EnviaCuerpoYLeeIdYUrl()
    {
        Respond("POST", "/payment-link/create", 200, """{"id":"01HXYZPL","url":"https://pay.pey.mx/01HXYZPL"}""");

        var link = await _pey.Gateway.CreatePaymentLinkAsync(new CreatePaymentLinkRequest(
            Amount: 10.50m,
            SuccessUrl: new Uri("https://local133.mx/app/link-de-pago/regreso?resultado=pagado"),
            BackUrl: new Uri("https://local133.mx/app/link-de-pago/regreso?resultado=cancelado"),
            ClientReferenceId: "2 tacos al pastor",
            Metadata: new Dictionary<string, string> { ["kind"] = "payment-link-poc", ["concept"] = "2 tacos al pastor" }));

        Assert.Equal(new CreatedPaymentLink("01HXYZPL", new Uri("https://pay.pey.mx/01HXYZPL")), link);
        var request = _pey.SingleRequest();
        Assert.Equal($"Bearer {PeyFixture.ApiKey}", request.Headers!["Authorization"].Single());
        var body = PeyFixture.BodyOf(request);
        Assert.Equal(10.50m, body.GetProperty("amount").GetDecimal());
        Assert.Equal("https://local133.mx/app/link-de-pago/regreso?resultado=pagado", body.GetProperty("successUrl").GetString());
        Assert.Equal("https://local133.mx/app/link-de-pago/regreso?resultado=cancelado", body.GetProperty("backUrl").GetString());
        Assert.False(body.GetProperty("hasTip").GetBoolean());
        Assert.Equal("2 tacos al pastor", body.GetProperty("clientReferenceId").GetString());
        Assert.Equal("2 tacos al pastor", body.GetProperty("metadata").GetProperty("concept").GetString());
        Assert.False(body.TryGetProperty("bankAccountId", out _)); // Pey no lo acepta en links
    }

    [Fact]
    public async Task CreatePaymentLink_SinOpcionales_LosOmite()
    {
        Respond("POST", "/payment-link/create", 200, """{"id":"PL1","url":"https://pay.pey.mx/PL1"}""");

        await _pey.Gateway.CreatePaymentLinkAsync(new CreatePaymentLinkRequest(
            1, new Uri("https://a.mx/ok"), new Uri("https://a.mx/back"), HasTip: true));

        var body = PeyFixture.BodyOf(_pey.SingleRequest());
        Assert.True(body.GetProperty("hasTip").GetBoolean());
        Assert.False(body.TryGetProperty("clientReferenceId", out _));
        Assert.False(body.TryGetProperty("metadata", out _));
    }

    [Theory]
    [InlineData("""{"id":"PL1"}""")]                         // falta url
    [InlineData("""{"id":"PL1","url":"/relativa"}""")]       // url no absoluta
    public async Task CreatePaymentLink_RespuestaIncompleta_LanzaPaymentGatewayException(string body)
    {
        Respond("POST", "/payment-link/create", 200, body);

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() => _pey.Gateway.CreatePaymentLinkAsync(
            new CreatePaymentLinkRequest(1, new Uri("https://a.mx/ok"), new Uri("https://a.mx/back"))));

        Assert.Contains("Respuesta inesperada", ex.Message);
    }

    [Fact]
    public async Task GetPaymentLink_LeeEstatusEnMinusculasYTransacciones()
    {
        Respond("GET", "/payment-link/01HXYZPL", 200, """
            {"id":"01HXYZPL","amount":100,"status":"partial","successUrl":"https://a.mx/ok","backUrl":"https://a.mx/back",
             "hasTip":true,"paymentMethods":["CARD"],"dateCreated":1704067200,"clientReferenceId":"ref-1",
             "metadata":{"concept":"Tacos"},
             "transactions":[{"transactionId":"01TX1","paymentMethod":"CARD","datePaid":1704067260,"amount":40,
                              "amountBreakdown":{"base":35,"tip":5},"applicationFee":null,"metadata":{"saleId":"9"}}]}
            """);

        var link = await _pey.Gateway.GetPaymentLinkAsync("01HXYZPL");

        Assert.Equal(PeyPaymentLinkStatus.Partial, link.Status);
        Assert.Equal(100m, link.Amount);
        Assert.True(link.HasTip);
        Assert.Equal(["CARD"], link.PaymentMethods);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1704067200), link.CreatedAt);
        Assert.Equal("ref-1", link.ClientReferenceId);
        Assert.Equal("Tacos", link.Metadata["concept"]);
        var tx = Assert.Single(link.Transactions);
        Assert.Equal("01TX1", tx.TransactionId);
        Assert.Equal(40m, tx.Amount);
        Assert.Equal(new GatewayAmountBreakdown(35, 5, null), tx.AmountBreakdown);
        Assert.Null(tx.ApplicationFee);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1704067260), tx.PaidAt);
        Assert.Equal("9", tx.Metadata["saleId"]);
    }

    [Theory]
    [InlineData("pending", PeyPaymentLinkStatus.Pending)]
    [InlineData("processing", PeyPaymentLinkStatus.Processing)]
    [InlineData("paid", PeyPaymentLinkStatus.Paid)]
    public async Task GetPaymentLink_MinimoYCadaEstatus(string wire, PeyPaymentLinkStatus expected)
    {
        Respond("GET", "/payment-link/PL1", 200, $$"""{"id":"PL1","amount":1,"status":"{{wire}}"}""");

        var link = await _pey.Gateway.GetPaymentLinkAsync("PL1");

        Assert.Equal(expected, link.Status);
        Assert.False(link.HasTip);
        Assert.Empty(link.PaymentMethods);
        Assert.Empty(link.Transactions);
        Assert.Empty(link.Metadata);
    }

    [Theory]
    [InlineData("""{"id":"PL1","amount":1}""")]                       // falta status
    [InlineData("""{"id":"PL1","amount":1,"status":"refunded"}""")]   // estatus que no conocemos
    [InlineData("""{"id":"PL1","amount":1,"status":"1"}""")]          // número en texto: no es un estatus
    public async Task GetPaymentLink_EstatusInvalido_LanzaPaymentGatewayException(string body)
    {
        Respond("GET", "/payment-link/PL1", 200, body);

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() => _pey.Gateway.GetPaymentLinkAsync("PL1"));

        Assert.Contains("Respuesta inesperada", ex.Message);
    }

    [Fact]
    public async Task GetTransaction_LeeObjetoCompleto()
    {
        Respond("GET", "/transaction/01HXYZTXN", 200, """
            {"id":"01HXYZTXN","status":"PAID","amount":102.5,"amountBreakdown":{"base":100,"tip":0,"fee":2.5},
             "currency":"MXN","paymentMethod":"CODI","payableType":"API_CODI_PN","payableId":"01HXYZ12",
             "applicationFee":7.5,"dateCreated":1704067200,"datePaid":1704067260,
             "metadata":{"clabe":"000000000000000000","bankCode":40012,"accountType":40,"description":"Pago en Tacos Pey",
                         "trackingKey":"0192191042","externalMetadata":{"saleId":"12345","flag":false}}}
            """);

        var tx = await _pey.Gateway.GetTransactionAsync("01HXYZTXN");

        Assert.Equal(PeyTransactionStatus.Paid, tx.Status);
        Assert.Equal(102.5m, tx.Amount);
        Assert.Equal(new GatewayAmountBreakdown(100, 0, 2.5m), tx.AmountBreakdown);
        Assert.Equal(PeyPaymentMethod.Codi, tx.PaymentMethod);
        Assert.Equal(PeyPayableType.ApiCodiPn, tx.PayableType);
        Assert.Equal(7.5m, tx.ApplicationFee);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1704067260), tx.PaidAt);
        Assert.Equal(40012, tx.Metadata.BankCode);
        Assert.Equal("0192191042", tx.Metadata.TrackingKey);
        Assert.Equal("12345", tx.Metadata.External["saleId"]);
        Assert.Equal("false", tx.Metadata.External["flag"]);
    }

    [Fact]
    public async Task GetTransaction_SinMetadata_UsaVacia()
    {
        Respond("GET", "/transaction/T1", 200, """{"id":"T1","status":"PENDING","amount":10}""");

        var tx = await _pey.Gateway.GetTransactionAsync("T1");

        Assert.Equal(PeyTransactionStatus.Pending, tx.Status);
        Assert.Null(tx.PaidAt);
        Assert.Empty(tx.Metadata.External);
    }

    [Fact]
    public async Task Listados_EnvianNextTokenYLoDevuelven()
    {
        Respond("GET", "/transaction/api-key", 200, """
            {"items":[{"id":"T1","status":"PAID","amount":100,"paymentMethod":"CARD","payableType":"PAYMENT_LINK"}],
             "nextToken":"eyJQSyI6+/="}
            """);

        var first = await _pey.Gateway.GetApiKeyTransactionsAsync();
        var second = await _pey.Gateway.GetApiKeyTransactionsAsync(first.NextToken);

        Assert.Equal("T1", Assert.Single(first.Items).Id);
        Assert.Equal("eyJQSyI6+/=", first.NextToken);
        var requests = _pey.Requests;
        Assert.Empty(requests[0].Query ?? new Dictionary<string, WireMock.Types.WireMockList<string>>());
        Assert.Equal("eyJQSyI6+/=", requests[1].Query!["nextToken"].Single());
        Assert.Contains("nextToken=eyJQSyI6%2B%2F%3D", requests[1].Url);
        Assert.Equal(PeyPayableType.PaymentLink, second.Items[0].PayableType);
    }

    [Fact]
    public async Task Listados_CuentaYQr_UsanSuRuta_YNextTokenNuloEsFin()
    {
        Respond("GET", "/transaction/bank-account/01JBAV", 200, """{"items":[],"nextToken":null}""");
        Respond("GET", "/transaction/codi/qr/AB12CD34EF", 200, """{"items":[{"id":"T9","status":"PAID","amount":0.01}]}""");

        var byAccount = await _pey.Gateway.GetBankAccountTransactionsAsync("01JBAV");
        var byQr = await _pey.Gateway.GetCodiQrTransactionsAsync("AB12CD34EF");

        Assert.Empty(byAccount.Items);
        Assert.Null(byAccount.NextToken);
        Assert.Equal(0.01m, Assert.Single(byQr.Items).Amount);
        Assert.Null(byQr.NextToken);
    }

    [Fact]
    public async Task Ids_SeEscapanEnLaRuta()
    {
        _pey.Server.Given(Request.Create().UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""{"items":[]}"""));

        await _pey.Gateway.GetCodiQrTransactionsAsync("A/B C");

        Assert.EndsWith("/transaction/codi/qr/A%2FB%20C", Assert.Single(_pey.SentUris));
    }

    // --- Errores: cada error.type del OpenAPI ⇒ excepción de dominio ---------------------------------

    public static TheoryData<string, int, Type> ErrorTypes => new()
    {
        { "INVALID_REQUEST", 400, typeof(PaymentGatewayValidationException) },
        { "INVALID_AMOUNT", 400, typeof(PaymentGatewayValidationException) },
        { "INVALID_PARAMETER", 400, typeof(PaymentGatewayValidationException) },
        { "INVALID_PARAMETERS", 400, typeof(PaymentGatewayValidationException) },
        { "INCOMPLETE_PARAMETERS", 400, typeof(PaymentGatewayValidationException) },
        { "BAD_REQUEST", 400, typeof(PaymentGatewayValidationException) },
        { "REQUEST_PARSING_ERROR", 400, typeof(PaymentGatewayValidationException) },
        { "INVALID_CLABE", 400, typeof(InvalidClabeException) },
        { "INVALID_STATE", 400, typeof(PaymentGatewayStateException) },
        { "PEY_CONSISTENCY_ERROR", 400, typeof(PaymentGatewayStateException) },
        { "CONCURRENT_UPDATE", 409, typeof(PaymentGatewayStateException) },
        { "NOT_FOUND", 404, typeof(PaymentGatewayNotFoundException) },
        { "BANK_ACCOUNT_NOT_FOUND", 404, typeof(PaymentGatewayNotFoundException) },
        { "AUTH_ERROR", 401, typeof(PaymentGatewayAuthenticationException) },
        { "FORBIDDEN", 403, typeof(PaymentGatewayAuthenticationException) },
        { "INTERNAL_SERVER_ERROR", 500, typeof(PaymentGatewayUnavailableException) },
        { "SERVICE_UNAVAILABLE", 503, typeof(PaymentGatewayUnavailableException) },
        { "FLAGS_UNAVAILABLE", 503, typeof(PaymentGatewayUnavailableException) },
        { "INVALID_URL", 400, typeof(PaymentGatewayValidationException) },
        { "INVALID_HAS_TIP", 400, typeof(PaymentGatewayValidationException) },
        { "INVALID_CLIENT_REFERENCE_ID", 400, typeof(PaymentGatewayValidationException) },
        { "PAYMENT_LINK_NOT_PENDING", 409, typeof(PaymentGatewayStateException) },
    };

    [Theory]
    [MemberData(nameof(ErrorTypes))]
    public async Task Error_SeMapeaPorTipo(string wireType, int status, Type expected)
    {
        Respond("POST", "/codi/qr", status, $$$"""{"error":{"message":"detalle de Pey","type":"{{{wireType}}}","requestId":"req-1"}}""");

        var ex = await Assert.ThrowsAnyAsync<PaymentGatewayException>(
            () => _pey.Gateway.CreateCodiQrAsync(new CreateCodiQrRequest(10, 600, Account)));

        Assert.IsType(expected, ex);
        Assert.Equal(wireType, JsonNamingPolicy.SnakeCaseUpper.ConvertName(ex.ErrorType.ToString()));
        Assert.Equal(status, ex.StatusCode);
        Assert.Equal("req-1", ex.RequestId);
        Assert.Equal("detalle de Pey", ex.GatewayMessage);
        Assert.Contains(wireType, ex.Message);
    }

    [Fact]
    public void ErrorTypes_CubreTodoElEnum()
    {
        var covered = ErrorTypes.Select(row => (string)row[0]).ToHashSet();
        var all = Enum.GetValues<PeyErrorType>().Where(t => t != PeyErrorType.Unknown)
            .Select(t => JsonNamingPolicy.SnakeCaseUpper.ConvertName(t.ToString()));
        Assert.All(all, wire => Assert.Contains(wire, covered));
    }

    [Theory]
    [InlineData(401, """{"message":"Unauthorized"}""", typeof(PaymentGatewayAuthenticationException), "Unauthorized")]
    [InlineData(404, "", typeof(PaymentGatewayNotFoundException), null)]
    [InlineData(400, """{"error":{"message":"nuevo","type":"SOMETHING_NEW"}}""", typeof(PaymentGatewayValidationException), "nuevo")]
    [InlineData(502, "<html>Bad Gateway</html>", typeof(PaymentGatewayUnavailableException), null)]
    [InlineData(418, "{}", typeof(PaymentGatewayException), null)]
    public async Task Error_SinTipoReconocido_SeMapeaPorCodigo(int status, string body, Type expected, string? gatewayMessage)
    {
        Respond("GET", "/bank-account/X", status, body);

        var ex = await Assert.ThrowsAnyAsync<PaymentGatewayException>(() => _pey.Gateway.GetBankAccountAsync("X"));

        Assert.IsType(expected, ex);
        Assert.Equal(PeyErrorType.Unknown, ex.ErrorType);
        Assert.Equal(status, ex.StatusCode);
        Assert.Equal(gatewayMessage, ex.GatewayMessage);
    }

    [Theory]
    [InlineData("""{"id":"X"}""")]                              // falta codiStatus
    [InlineData("""{"id":"X","codiStatus":"SUSPENDED"}""")]     // estatus que no conocemos
    [InlineData("no es json")]
    public async Task RespuestaExitosaInesperada_LanzaPaymentGatewayException(string body)
    {
        Respond("GET", "/bank-account/X", 200, body);

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() => _pey.Gateway.GetBankAccountAsync("X"));

        Assert.Contains("Respuesta inesperada", ex.Message);
    }

    // --- Reintentos ---------------------------------------------------------------------------------

    [Fact]
    public async Task Get_SeReintentaEn500()
    {
        _pey.Server.Given(Request.Create().WithPath("/transaction/T1").UsingGet())
            .InScenario("get").WillSetStateTo("ok")
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("""{"error":{"type":"INTERNAL_SERVER_ERROR"}}"""));
        _pey.Server.Given(Request.Create().WithPath("/transaction/T1").UsingGet())
            .InScenario("get").WhenStateIs("ok")
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""{"id":"T1","status":"PAID","amount":1}"""));

        var tx = await _pey.Gateway.GetTransactionAsync("T1");

        Assert.Equal("T1", tx.Id);
        Assert.Equal(2, _pey.Requests.Count);
    }

    [Fact]
    public async Task Post_NoSeReintentaEn500()
    {
        Respond("POST", "/codi/pn", 500, """{"error":{"message":"Error creating CoDi push notification","type":"INTERNAL_SERVER_ERROR"}}""");

        await Assert.ThrowsAsync<PaymentGatewayUnavailableException>(() =>
            _pey.Gateway.CreateCodiPushNotificationAsync(new CreateCodiPushNotificationRequest(1, "5512345678", Account)));

        Assert.Single(_pey.Requests); // un segundo intento podría mandar dos push al pagador
    }

    [Fact]
    public async Task Post_SeReintentaEn503()
    {
        _pey.Server.Given(Request.Create().WithPath("/codi/qr").UsingPost())
            .InScenario("post").WillSetStateTo("ok")
            .RespondWith(Response.Create().WithStatusCode(503).WithBody("""{"error":{"type":"SERVICE_UNAVAILABLE"}}"""));
        _pey.Server.Given(Request.Create().WithPath("/codi/qr").UsingPost())
            .InScenario("post").WhenStateIs("ok")
            .RespondWith(Response.Create().WithStatusCode(201).WithBody("""{"id":"IDC","qrString":"x"}"""));

        var qr = await _pey.Gateway.CreateCodiQrAsync(new CreateCodiQrRequest(10, 600, Account));

        Assert.Equal("IDC", qr.Id);
        var requests = _pey.Requests;
        Assert.Equal(2, requests.Count);
        Assert.Equal(requests[0].Body, requests[1].Body); // el cuerpo se reenvía completo
    }

    [Fact]
    public async Task Post_ConTimeout_NoSeReintenta()
    {
        using var pey = new PeyFixture(attemptTimeout: TimeSpan.FromMilliseconds(300));
        pey.Server.Given(Request.Create().WithPath("/codi/qr").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201).WithBody("""{"id":"IDC","qrString":"x"}""").WithDelay(TimeSpan.FromSeconds(1)));

        var ex = await Assert.ThrowsAsync<PaymentGatewayUnavailableException>(() =>
            pey.Gateway.CreateCodiQrAsync(new CreateCodiQrRequest(10, 600, Account)));

        Assert.Null(ex.StatusCode);
        Assert.True(ex.IsTransient);
        // WireMock registra la petición al terminar su retraso: esperar a que aparezca y a que un reintento
        // (que también tardaría 1 s) tuviera tiempo de registrarse.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (pey.Requests.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        await Task.Delay(TimeSpan.FromSeconds(1.5));
        Assert.Single(pey.Requests);
    }

    [Fact]
    public async Task CancelacionDelLlamador_NoSeConvierteEnError()
    {
        _pey.Server.Given(Request.Create().WithPath("/transaction/T1").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{}").WithDelay(TimeSpan.FromSeconds(2)));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _pey.Gateway.GetTransactionAsync("T1", cts.Token));
    }

    // --- Validación previa y configuración ----------------------------------------------------------

    public static TheoryData<Func<IPaymentGateway, Task>> InvalidRequests => new()
    {
        g => g.CreateCodiQrAsync(new CreateCodiQrRequest(10, ExpirationSeconds: 100, BankAccountId: Account)),
        g => g.CreateCodiQrAsync(new CreateCodiQrRequest(-1, 600, Account)),
        g => g.CreateCodiQrAsync(new CreateCodiQrRequest(1.005m, 600, Account)),
        g => g.CreateCodiQrAsync(new CreateCodiQrRequest(10, 600, Account, NumRef: 10_000_000)),
        g => g.CreateCodiQrAsync(new CreateCodiQrRequest(10, 600, BankAccountId: " ")),
        g => g.CreateCodiQrAsync(new CreateCodiQrRequest(10, 600, BankAccountId: "")),
        g => g.CreateCodiQrAsync(new CreateCodiQrRequest(10, 600, BankAccountId: null!)),
        g => g.CreateCodiQrAsync(new CreateCodiQrRequest(10, 600, Account,
            Metadata: Enumerable.Range(0, 51).ToDictionary(i => $"k{i}", i => "v"))),
        g => g.CreateCodiQrAsync(new CreateCodiQrRequest(10, 600, Account,
            Metadata: new Dictionary<string, string> { [new string('k', 41)] = "v" })),
        g => g.CreateCodiQrAsync(new CreateCodiQrRequest(10, 600, Account,
            Metadata: new Dictionary<string, string> { ["k"] = new string('v', 501) })),
        g => g.CreateCodiPushNotificationAsync(new CreateCodiPushNotificationRequest(10, "551234567", Account)),
        g => g.CreateCodiPushNotificationAsync(new CreateCodiPushNotificationRequest(0, "5512345678", Account)),
        g => g.CreateCodiPushNotificationAsync(new CreateCodiPushNotificationRequest(1_000_000.01m, "5512345678", Account)),
        g => g.CreateCodiPushNotificationAsync(new CreateCodiPushNotificationRequest(10, "5512345678", Account, ExpirationSeconds: 0)),
        g => g.CreateCodiPushNotificationAsync(new CreateCodiPushNotificationRequest(10, "5512345678", BankAccountId: "")),
        g => g.CreateCodiPushNotificationAsync(new CreateCodiPushNotificationRequest(10, "5512345678", BankAccountId: null!)),
        g => g.RegisterBankAccountAsync(new RegisterBankAccountRequest("Alias", "Nombre", "01218001582622571", PeyEntity.Person)),
        g => g.RegisterBankAccountAsync(new RegisterBankAccountRequest(" ", "Nombre", "012180015826225713", PeyEntity.Person)),
        g => g.RegisterBankAccountAsync(new RegisterBankAccountRequest("Alias", new string('n', 256), "012180015826225713", PeyEntity.Business)),
        g => g.GetBankAccountAsync(""),
        g => g.GetCodiQrTransactionsAsync(" "),
        g => g.CreatePaymentLinkAsync(new CreatePaymentLinkRequest(10, new Uri("http://a.mx/ok"), new Uri("https://a.mx/back"))),
        g => g.CreatePaymentLinkAsync(new CreatePaymentLinkRequest(10, new Uri("https://a.mx/ok"), new Uri("/back", UriKind.Relative))),
        g => g.CreatePaymentLinkAsync(new CreatePaymentLinkRequest(0, new Uri("https://a.mx/ok"), new Uri("https://a.mx/back"))),
        g => g.CreatePaymentLinkAsync(new CreatePaymentLinkRequest(1.005m, new Uri("https://a.mx/ok"), new Uri("https://a.mx/back"))),
        g => g.CreatePaymentLinkAsync(new CreatePaymentLinkRequest(10, new Uri("https://a.mx/ok"), new Uri("https://a.mx/back"),
            ClientReferenceId: new string('r', 256))),
        g => g.CreatePaymentLinkAsync(new CreatePaymentLinkRequest(10, new Uri("https://a.mx/ok"), new Uri("https://a.mx/back"),
            Metadata: new Dictionary<string, string> { ["k"] = new string('v', 501) })),
        g => g.GetPaymentLinkAsync(" "),
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task PeticionFueraDeReglas_NoLlamaAPey(Func<IPaymentGateway, Task> call)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => call(_pey.Gateway));

        Assert.Empty(_pey.Requests);
    }

    [Fact]
    public async Task SinApiKey_FallaAlUsarse()
    {
        using var pey = new PeyFixture(apiKey: null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => pey.Gateway.GetTransactionAsync("T1"));

        Assert.Contains("Pey:ApiKey", ex.Message);
        Assert.Empty(pey.Requests);
    }
}
