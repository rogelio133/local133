using Local133.Application.Sales;

namespace Local133.Infrastructure.Sales;

/// <summary>Sin tiempo real fuera del sitio (jobs, <c>dotnet ef</c>); el sitio registra su notificador de SignalR.</summary>
internal sealed class NullSaleNotifier : ISaleNotifier
{
    public Task PaymentReceivedAsync(int merchantId, int saleId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
