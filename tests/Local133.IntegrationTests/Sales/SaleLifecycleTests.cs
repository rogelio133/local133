using System.Net;
using System.Text.Json;
using Local133.Application.Sales;
using Local133.Domain.Sales;
using Local133.Infrastructure.Jobs;
using Local133.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Local133.IntegrationTests.Sales;

/// <summary>Paso 2.5: pantalla de la venta (pendiente / pagada / cancelada), cancelación, expiración y estatus por API.</summary>
public class SaleLifecycleTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    // --- Pantalla de la venta ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Pendiente_muestra_el_QR_la_espera_del_pago_y_el_boton_de_cancelar()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);

        var html = await GetHtmlAsync(client, SaleUrl(sale.Id));

        Assert.Contains("data-sale-status=\"Pending\"", html);
        Assert.Contains($"data-status-url=\"/api/sales/{sale.Id}\"", html);
        Assert.Contains("data-hub-url=\"/hubs/ventas\"", html);
        Assert.Contains("data-sale-qr", html);
        Assert.Contains("data-waiting", html);
        Assert.Contains("data-cancel-sale", html);
        Assert.Contains($"action=\"/app/cobrar/venta/{sale.Id}/cancelar\"", html);
        Assert.Contains("CoDi no permite anular un QR", html);
        Assert.Contains("/lib/microsoft-signalr/dist/browser/signalr.min", html);
        Assert.Contains("/js/sale-qr", html);
        Assert.Contains("class=\"d-none\" data-paid-only", html); // «Pago recibido» oculto hasta que llegue
    }

    [Fact]
    public async Task Pagada_muestra_Pago_recibido_sin_QR_ni_scripts_de_espera()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);
        var paidAt = new DateTimeOffset(2026, 11, 12, 21, 45, 0, TimeSpan.Zero); // 3:45 p. m. en CDMX
        await PostWebhookAsync(QrPaymentBodyFor(sale, paidAt: paidAt));

        var html = await GetHtmlAsync(client, SaleUrl(sale.Id));

        Assert.Contains("data-sale-status=\"Paid\"", html);
        Assert.Contains("Pago recibido", html);
        Assert.DoesNotContain("class=\"d-none\" data-paid-only", html);
        Assert.Contains("data-paid-at>12 de noviembre, 3:45", html);
        Assert.DoesNotContain("data-sale-qr", html);
        Assert.DoesNotContain("data-cancel-sale", html);
        Assert.DoesNotContain("signalr.min", html);
        Assert.DoesNotContain("/js/sale-qr", html);
    }

    // --- Cancelación ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Cancelar_una_pendiente_la_deja_cancelada_y_lo_muestra()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);

        var response = await CancelAsync(client, sale.Id);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(SaleUrl(sale.Id), Location(response));
        var cancelled = await LoadSaleAsync(sale.Id);
        Assert.Equal(SaleStatus.Cancelled, cancelled.Status);
        Assert.NotNull(cancelled.CancelledAt);

        var html = await GetHtmlAsync(client, SaleUrl(sale.Id));
        Assert.Contains("data-sale-status=\"Cancelled\"", html);
        Assert.DoesNotContain("data-sale-notice", html); // el bloque de cancelado ya lo explica
        Assert.Contains("data-cancelled-only", html);
        Assert.DoesNotContain("data-sale-qr", html);
        Assert.DoesNotContain("data-cancel-sale", html);
        // Sigue escuchando: si el cliente ya había escaneado el QR, el pago todavía puede llegar.
        Assert.Contains("/js/sale-qr", html);
        Assert.Empty(Factory.Gateway.CodiQrRequests.Skip(1)); // cancelar no llama a Pey
    }

    [Fact]
    public async Task Cancelar_una_pagada_no_la_cambia_y_avisa()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);
        await PostWebhookAsync(QrPaymentBodyFor(sale));

        await CancelAsync(client, sale.Id);

        Assert.Equal(SaleStatus.Paid, (await LoadSaleAsync(sale.Id)).Status);
        Assert.Contains("Este cobro ya se pagó; no se puede cancelar.", await GetHtmlAsync(client, SaleUrl(sale.Id)));
    }

    [Fact]
    public async Task Cancelar_una_vencida_por_tiempo_no_la_cambia_y_avisa()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);
        await UpdateSaleAsync(sale.Id, s => s.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1));

        await CancelAsync(client, sale.Id);

        var current = await LoadSaleAsync(sale.Id);
        Assert.Equal(SaleStatus.Pending, current.Status); // la vence el job
        Assert.Null(current.CancelledAt);
        Assert.Contains("Este cobro ya expiró", await GetHtmlAsync(client, SaleUrl(sale.Id)));
    }

    [Fact]
    public async Task Cancelar_dos_veces_avisa_que_ya_estaba_cancelado()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);

        await CancelAsync(client, sale.Id);
        await CancelAsync(client, sale.Id);

        Assert.Contains("Este cobro ya estaba cancelado.", await GetHtmlAsync(client, SaleUrl(sale.Id)));
    }

    [Fact]
    public async Task No_se_puede_cancelar_la_venta_de_otro_vendedor()
    {
        var owner = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(owner);
        var other = await ActiveMerchantAsync(OtherEmail);

        var response = await CancelAsync(other, sale.Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(SaleStatus.Pending, (await LoadSaleAsync(sale.Id)).Status);
    }

    [Fact]
    public async Task Cancelar_sin_token_antiforgery_responde_400()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);

        var response = await client.PostAsync($"{SaleUrl(sale.Id)}/cancelar", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(SaleStatus.Pending, (await LoadSaleAsync(sale.Id)).Status);
    }

    // --- Expiración -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task El_job_vence_solo_las_pendientes_con_el_QR_vencido()
    {
        var client = await ActiveMerchantAsync(Email);
        var due = await ChargeAsync(client);
        var current = await ChargeAsync(client);
        var paid = await ChargeAsync(client);
        var cancelled = await ChargeAsync(client);
        var past = DateTimeOffset.UtcNow.AddSeconds(-1);
        await UpdateSaleAsync(due.Id, s => s.ExpiresAt = past);
        await UpdateSaleAsync(paid.Id, s => { s.ExpiresAt = past; s.MarkPaid(past); });
        await UpdateSaleAsync(cancelled.Id, s => { s.ExpiresAt = past; s.Cancel(past.AddSeconds(-10)); });

        int expired;
        using (var scope = Factory.Services.CreateScope())
        {
            expired = await scope.ServiceProvider.GetRequiredService<SaleExpirationJob>().RunAsync(CancellationToken.None);
        }

        Assert.Equal(1, expired);
        Assert.Equal(SaleStatus.Expired, (await LoadSaleAsync(due.Id)).Status);
        Assert.Equal(SaleStatus.Pending, (await LoadSaleAsync(current.Id)).Status);
        Assert.Equal(SaleStatus.Paid, (await LoadSaleAsync(paid.Id)).Status);
        Assert.Equal(SaleStatus.Cancelled, (await LoadSaleAsync(cancelled.Id)).Status);

        var html = await GetHtmlAsync(client, SaleUrl(due.Id));
        Assert.Contains("data-sale-status=\"Expired\"", html);
        Assert.Contains("data-expired=\"true\"", html);
    }

    [Fact]
    public async Task El_job_expira_ventas_de_todos_los_vendedores_sin_sesion()
    {
        var first = await ChargeAsync(await ActiveMerchantAsync(Email));
        var second = await ChargeAsync(await ActiveMerchantAsync(OtherEmail));
        var past = DateTimeOffset.UtcNow.AddMinutes(-1);
        await UpdateSaleAsync(first.Id, s => s.ExpiresAt = past);
        await UpdateSaleAsync(second.Id, s => s.ExpiresAt = past);

        using var scope = Factory.Services.CreateScope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<ISaleExpiration>().ExpireDueAsync());
    }

    // --- GET /api/sales/{id} ----------------------------------------------------------------------------------------

    [Fact]
    public async Task La_API_de_estatus_devuelve_la_venta_propia()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);

        var pending = await GetStatusAsync(client, sale.Id);
        Assert.Equal("Pending", pending.GetProperty("status").GetString());
        Assert.Equal(sale.Id, pending.GetProperty("id").GetInt32());
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("paidAt").ValueKind);

        await PostWebhookAsync(QrPaymentBodyFor(sale));
        var paid = await GetStatusAsync(client, sale.Id);
        Assert.Equal("Paid", paid.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, paid.GetProperty("paidAt").ValueKind);
        Assert.False(string.IsNullOrEmpty(paid.GetProperty("paidAtText").GetString()));
    }

    [Fact]
    public async Task La_API_reporta_vencida_una_pendiente_con_el_QR_vencido()
    {
        var client = await ActiveMerchantAsync(Email);
        var sale = await ChargeAsync(client);
        await UpdateSaleAsync(sale.Id, s => s.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1));

        Assert.Equal("Expired", (await GetStatusAsync(client, sale.Id)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task La_API_no_muestra_ventas_de_otro_vendedor()
    {
        var sale = await ChargeAsync(await ActiveMerchantAsync(Email));
        var other = await ActiveMerchantAsync(OtherEmail);

        var response = await other.GetAsync($"/api/sales/{sale.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task La_API_sin_sesion_responde_401_y_al_admin_403()
    {
        var sale = await ChargeAsync(await ActiveMerchantAsync(Email));

        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateClient().GetAsync($"/api/sales/{sale.Id}")).StatusCode);

        await CreateConfirmedUserAsync("admin@prueba.mx", Application.Auth.Roles.Admin);
        var admin = CreateClient();
        await LoginAsync(admin, "admin@prueba.mx");
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/sales/{sale.Id}")).StatusCode);
    }

    private static Task<HttpResponseMessage> CancelAsync(HttpClient client, int saleId) =>
        PostFormAsync(client, TokenPage, new Dictionary<string, string>(), postUrl: $"{SaleUrl(saleId)}/cancelar");

    private static async Task<JsonElement> GetStatusAsync(HttpClient client, int saleId)
    {
        var response = await client.GetAsync($"/api/sales/{saleId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }
}
