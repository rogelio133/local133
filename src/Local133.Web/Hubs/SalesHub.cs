using System.Globalization;
using Local133.Application.Auth;
using Local133.Application.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Local133.Web.Hubs;

/// <summary>
/// Tiempo real del vendedor (<c>/hubs/ventas</c>): cada conexión entra al grupo de su vendedor (claim
/// <see cref="AppClaimTypes.MerchantId"/>) y recibe <see cref="ISaleNotifier.PaymentReceivedEvent"/>. El cliente no
/// invoca nada: solo escucha.
/// </summary>
[Authorize(Roles = Roles.Merchant)]
public sealed class SalesHub : Hub
{
    public const string Path = "/hubs/ventas";

    public static string GroupFor(int merchantId) => $"merchant-{merchantId.ToString(CultureInfo.InvariantCulture)}";

    public override async Task OnConnectedAsync()
    {
        var claim = Context.User?.FindFirst(AppClaimTypes.MerchantId)?.Value;
        if (!int.TryParse(claim, NumberStyles.None, CultureInfo.InvariantCulture, out var merchantId))
        {
            // Falla cerrada, como HttpTenantContext: sin vendedor no hay grupo.
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(merchantId));
        await base.OnConnectedAsync();
    }
}

/// <summary>Avisa al grupo del vendedor que una venta recibió un pago (la vista confirma con <c>GET /api/sales/{id}</c>).</summary>
public sealed class SignalRSaleNotifier(IHubContext<SalesHub> hub) : ISaleNotifier
{
    public Task PaymentReceivedAsync(int merchantId, int saleId, CancellationToken cancellationToken = default) =>
        hub.Clients.Group(SalesHub.GroupFor(merchantId))
            .SendAsync(ISaleNotifier.PaymentReceivedEvent, new { saleId }, cancellationToken);
}
