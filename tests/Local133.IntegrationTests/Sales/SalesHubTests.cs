using System.Net;
using System.Text.Json;
using Local133.Application.Sales;
using Local133.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.SignalR.Client;

namespace Local133.IntegrationTests.Sales;

/// <summary>
/// Paso 2.5: tiempo real por SignalR (<c>/hubs/ventas</c>). Cliente real sobre TestServer (long polling con la cookie
/// de sesión): el vendedor recibe el aviso de su pago y otro vendedor no.
/// </summary>
public class SalesHubTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task El_vendedor_recibe_PaymentReceived_de_su_venta_y_otro_vendedor_no()
    {
        var (ownerClient, ownerCookies) = await SignedInAsync(Email);
        var sale = await ChargeAsync(ownerClient);
        var (_, otherCookies) = await SignedInAsync(OtherEmail);

        var ownerReceived = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherReceived = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var owner = await ConnectAsync(ownerCookies, ownerReceived);
        await using var other = await ConnectAsync(otherCookies, otherReceived);

        Assert.Equal("Processed", await WebhookStatusAsync(await PostWebhookAsync(QrPaymentBodyFor(sale))));

        Assert.Equal(sale.Id, await ownerReceived.Task.WaitAsync(Wait));
        // Damos tiempo a que llegara un aviso equivocado.
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.False(otherReceived.Task.IsCompleted);
    }

    [Fact]
    public async Task Sin_sesion_no_se_puede_conectar_al_hub()
    {
        await using var connection = Build(new CookieContainer());

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    /// <summary>Vendedor activo con sesión; las cookies quedan en un contenedor propio para pasarlas al cliente de SignalR.</summary>
    private async Task<(HttpClient Client, CookieContainer Cookies)> SignedInAsync(string email)
    {
        await ActiveMerchantAsync(email); // crea vendedor + cuenta (su cliente no se usa)
        var cookies = new CookieContainer();
        var client = new HttpClient(new CookieContainerHandler(cookies) { InnerHandler = Factory.Server.CreateHandler() })
        {
            BaseAddress = new Uri("https://localhost"),
        };
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(client, email)).StatusCode);
        return (client, cookies);
    }

    private async Task<HubConnection> ConnectAsync(CookieContainer cookies, TaskCompletionSource<int> received)
    {
        var connection = Build(cookies);
        connection.On<JsonElement>(ISaleNotifier.PaymentReceivedEvent, e => received.TrySetResult(e.GetProperty("saleId").GetInt32()));
        await connection.StartAsync();
        return connection;
    }

    private HubConnection Build(CookieContainer cookies) => new HubConnectionBuilder()
        .WithUrl("https://localhost/hubs/ventas", options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => new CookieContainerHandler(cookies) { InnerHandler = Factory.Server.CreateHandler() };
        })
        .Build();
}
