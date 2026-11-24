using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Local133.Application.Sales;
using Local133.Domain.BankAccounts;
using Local133.Domain.Merchants;
using Local133.Domain.Pey;
using Local133.Domain.Sales;
using Local133.Infrastructure.Persistence;
using Local133.Infrastructure.Webhooks;
using Local133.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Local133.IntegrationTests.Sales;

/// <summary>
/// Ayudas para el ciclo de una venta: vendedor activo, venta con QR (Pey simulado), webhooks firmados como Pey y
/// lectura de la base.
/// </summary>
public abstract class SaleFlowTestBase(Local133WebFactory factory) : WebTestBase(factory)
{
    protected const string Email = "vendedor@prueba.mx";
    protected const string OtherEmail = "otro@prueba.mx";
    protected const string TestClabe = "012180015826225713";
    protected const string WebhookUrl = "/api/webhooks/pey";

    /// <summary>Página con un formulario antiforgery cualquiera (el token sirve para todo el sitio).</summary>
    protected const string TokenPage = "/Identity/Account/Manage";

    /// <summary>Vendedor activo con cuenta bancaria activa (o en el estatus indicado), ya con sesión.</summary>
    protected async Task<HttpClient> ActiveMerchantAsync(
        string email, PeyCodiStatus codiStatus = PeyCodiStatus.Active, MerchantStatus status = MerchantStatus.Active)
    {
        await CreateMerchantAsync(email);
        await WithDbAsync(async db =>
        {
            var merchant = await MerchantOfAsync(db, email);
            merchant.Status = status;
            merchant.BusinessName = "Tacos Don Ramón";
            db.BankAccounts.Add(BankAccount.Submit(
                merchant.Id, "Ramón Orraca Lascurain", TestClabe, "Puesto", PeyEntity.Person,
                $"01PEY{merchant.Id:D10}", codiStatus, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        });

        var client = CreateClient();
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(client, email)).StatusCode);
        return client;
    }

    /// <summary>Cobra con el formulario real (Pey simulado) y devuelve la venta creada.</summary>
    protected async Task<Sale> ChargeAsync(HttpClient client, string amount = "150.00", string concept = "2 tacos al pastor")
    {
        var response = await PostFormAsync(client, "/app/cobrar", new Dictionary<string, string>
        {
            ["Input.Amount"] = amount,
            ["Input.Concept"] = concept,
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var id = int.Parse(Location(response)!.Split('/').Last(), CultureInfo.InvariantCulture);
        return await LoadSaleAsync(id);
    }

    /// <summary>Cobra al celular con el formulario real (Pey simulado); devuelve la respuesta del POST.</summary>
    protected Task<HttpResponseMessage> PostPushChargeAsync(
        HttpClient client, string phoneNumber = "55 1234 5678", string amount = "150.00", string concept = "2 tacos al pastor") =>
        PostFormAsync(client, "/app/cobrar", new Dictionary<string, string>
        {
            ["Input.Method"] = nameof(ChargeMethod.PushNotification),
            ["Input.Amount"] = amount,
            ["Input.Concept"] = concept,
            ["Input.PhoneNumber"] = phoneNumber,
        });

    /// <summary>Cobra al celular con el formulario real (Pey simulado) y devuelve la venta creada.</summary>
    protected async Task<Sale> ChargeByPhoneAsync(HttpClient client, string phoneNumber = "55 1234 5678", string amount = "150.00")
    {
        var response = await PostPushChargeAsync(client, phoneNumber, amount);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var id = int.Parse(Location(response)!.Split('/').Last(), CultureInfo.InvariantCulture);
        return await LoadSaleAsync(id);
    }

    /// <summary>Producto vigente en el catálogo del vendedor; devuelve su id.</summary>
    protected Task<int> CreateProductAsync(string email, string name, decimal price) => WithDbAsync(async db =>
    {
        var merchant = await MerchantOfAsync(db, email);
        var product = Domain.Products.Product.Create(merchant.Id, name, null, price, DateTimeOffset.UtcNow);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    });

    protected Task<Sale> LoadSaleAsync(int id) =>
        WithDbAsync(db => db.Sales.AsNoTracking().SingleAsync(s => s.Id == id));

    protected Task UpdateSaleAsync(int id, Action<Sale> change) => WithDbAsync(async db =>
    {
        change(await db.Sales.SingleAsync(s => s.Id == id));
        await db.SaveChangesAsync();
    });

    protected Task UpdateMerchantAsync(string email, Action<Merchant> change) => WithDbAsync(async db =>
    {
        change(await MerchantOfAsync(db, email));
        await db.SaveChangesAsync();
    });

    /// <summary>
    /// Venta insertada directo en la base (para fechas y estatus a la medida): con QR o, con <paramref name="phone"/>,
    /// al celular; con <paramref name="items"/> (nombre, precio, cantidad) es una venta con productos y el monto es su
    /// suma.
    /// </summary>
    protected Task<Sale> InsertSaleAsync(
        string email, decimal amount, DateTimeOffset createdAt, SaleStatus status = SaleStatus.Pending,
        DateTimeOffset? expiresAt = null, string concept = "Tacos Don Ramón · 2 tacos", string? phone = null,
        IReadOnlyList<(string Name, decimal Price, int Quantity)>? items = null) => WithDbAsync(async db =>
    {
        var merchant = await MerchantOfAsync(db, email);
        var folio = (await db.Sales.IgnoreQueryFilters().Where(s => s.MerchantId == merchant.Id).MaxAsync(s => (int?)s.Folio) ?? 0) + 1;
        var lines = items?.Select(i => new SaleItem
        {
            MerchantId = merchant.Id, Name = i.Name, UnitPrice = i.Price, Quantity = i.Quantity, Amount = i.Price * i.Quantity,
        }).ToList();
        if (lines is { Count: > 0 })
        {
            amount = lines.Sum(i => i.Amount);
        }

        var expires = expiresAt ?? createdAt.AddMinutes(10);
        Sale sale;
        if (phone is null)
        {
            sale = Sale.StartDirect(merchant.Id, folio, amount, concept, expires, createdAt, lines);
            var idc = $"IDC{Guid.NewGuid():N}"[..20];
            sale.AttachQr(idc, FakePaymentGateway.QrStringFor(idc));
        }
        else
        {
            sale = Sale.StartPush(merchant.Id, folio, amount, concept, phone, expires, createdAt, lines);
            sale.AttachPushNotification(NewTransactionId(), null);
        }

        sale.Status = status;
        if (status == SaleStatus.Cancelled)
        {
            sale.CancelledAt = createdAt.AddMinutes(1);
        }

        db.Sales.Add(sale);
        await db.SaveChangesAsync();
        return sale;
    });

    /// <summary>Pago recibido de una venta insertada; la deja pagada.</summary>
    protected Task AddPaymentAsync(Sale sale, decimal amount, DateTimeOffset paidAt) => WithDbAsync(async db =>
    {
        var tracked = await db.Sales.IgnoreQueryFilters().SingleAsync(s => s.Id == sale.Id);
        db.Payments.Add(Domain.Payments.Payment.Record(
            tracked.MerchantId, tracked.Id, NewTransactionId(), amount, paidAt, "TRK123", 40012, DateTimeOffset.UtcNow));
        tracked.MarkPaid(paidAt);
        await db.SaveChangesAsync();
    });

    protected Task<int> MerchantIdAsync(string email) => WithDbAsync(async db => (await MerchantOfAsync(db, email)).Id);

    protected async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        using var scope = Factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    protected async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    protected static async Task<Merchant> MerchantOfAsync(AppDbContext db, string email)
    {
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        return await db.Merchants.SingleAsync(m => m.UserId == userId);
    }

    protected static async Task<string> GetHtmlAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    protected static string SaleUrl(int saleId) => $"/app/cobrar/venta/{saleId}";

    // --- Webhooks ---------------------------------------------------------------------------------------------------

    /// <summary>POST al webhook firmado como Pey (Standard Webhooks con el secreto de la fábrica).</summary>
    protected async Task<HttpResponseMessage> PostWebhookAsync(
        string body, string? webhookId = null, DateTimeOffset? sentAt = null, string secret = Local133WebFactory.WebhookSecret,
        HttpClient? client = null)
    {
        webhookId ??= NewWebhookId();
        var timestamp = (sentAt ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(StandardWebhookVerifier.IdHeader, webhookId);
        request.Headers.Add(StandardWebhookVerifier.TimestampHeader, timestamp);
        request.Headers.Add(StandardWebhookVerifier.SignatureHeader, StandardWebhookVerifier.ComputeSignature(secret, webhookId, timestamp, body));
        return await (client ?? CreateClient()).SendAsync(request);
    }

    protected static string NewWebhookId() => $"msg_{Guid.NewGuid():N}";

    protected static string NewTransactionId() => $"01TX{Guid.NewGuid():N}"[..26].ToUpperInvariant();

    /// <summary>Payload de <c>codi.qr.payment_received</c> como lo manda Pey (docs.pey.mx).</summary>
    protected static string QrPaymentBody(
        string transactionId, string? codiQrId, decimal amount, IReadOnlyDictionary<string, string>? external,
        DateTimeOffset? paidAt = null, string? bankAccountId = null) =>
        JsonSerializer.Serialize(new
        {
            amount,
            apiKeyId = "01M3QMMR6X9CBVDY1SF3G8QWNW",
            bankAccountId,
            codiQrId,
            concept = "Tacos Don Ramón · 2 tacos al pastor",
            datePaid = (paidAt ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds(),
            metadata = new
            {
                clabe = "000000000000000000",
                bankCode = 40012,
                accountType = 40,
                description = "Tacos Don Ramón · 2 tacos al pastor",
                trackingKey = "0192191042",
                externalMetadata = external,
            },
            numRef = "1",
            transactionId,
            type = PeyWebhookEventTypes.CodiQrPaymentReceived,
        });

    /// <summary>Pago de una venta tal como lo mandaría Pey (IDC y metadata de la venta).</summary>
    protected static string QrPaymentBodyFor(Sale sale, string? transactionId = null, decimal? amount = null, DateTimeOffset? paidAt = null) =>
        QrPaymentBody(transactionId ?? NewTransactionId(), sale.PeyQrId, amount ?? sale.Amount, SaleMetadataFor(sale), paidAt);

    /// <summary>Payload de <c>codi.pn.payment_received</c> como lo manda Pey (docs.pey.mx): sin <c>codiQrId</c>.</summary>
    protected static string PushPaymentBody(
        string transactionId, decimal amount, IReadOnlyDictionary<string, string>? external, DateTimeOffset? paidAt = null) =>
        JsonSerializer.Serialize(new
        {
            amount,
            apiKeyId = "01M3QMMR6X9CBVDY1SF3G8QWNW",
            bankAccountId = "01PEY0000000001",
            concept = "Tacos Don Ramón · 2 tacos al pastor",
            datePaid = (paidAt ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds(),
            metadata = new
            {
                clabe = "000000000000000000",
                bankCode = 40012,
                accountType = 40,
                description = "Tacos Don Ramón · 2 tacos al pastor",
                trackingKey = "0192191043",
                externalMetadata = external,
            },
            numRef = "1234567",
            transactionId,
            type = PeyWebhookEventTypes.CodiPnPaymentReceived,
        });

    /// <summary>Pago de un cobro por celular tal como lo mandaría Pey (su transacción y la metadata de la venta).</summary>
    protected static string PushPaymentBodyFor(Sale sale, string? transactionId = null, decimal? amount = null, DateTimeOffset? paidAt = null) =>
        PushPaymentBody(transactionId ?? sale.PeyTransactionId ?? NewTransactionId(), amount ?? sale.Amount, SaleMetadataFor(sale), paidAt);

    protected static Dictionary<string, string> SaleMetadataFor(Sale sale) => new()
    {
        [SaleMetadata.KindKey] = SaleMetadata.SaleKind,
        [SaleMetadata.MerchantIdKey] = sale.MerchantId.ToString(CultureInfo.InvariantCulture),
        [SaleMetadata.SaleIdKey] = sale.Id.ToString(CultureInfo.InvariantCulture),
    };

    protected static async Task<string> WebhookStatusAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("status").GetString()!;
    }
}
