using System.Collections.Concurrent;

using Local133.Application.Payments;
using Local133.Domain.Pey;

namespace Local133.IntegrationTests.Infrastructure;

/// <summary>
/// Pasarela falsa para las pruebas del sitio: reemplaza a Pey en <see cref="Local133WebFactory"/>, así ninguna
/// prueba llama a producción aunque los user-secrets tengan <c>Pey:ApiKey</c>. Registra cada alta y responde lo
/// configurado (se restablece con <see cref="Reset"/>).
/// </summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    private int _sequence;

    /// <summary>Altas recibidas, en orden.</summary>
    public ConcurrentQueue<RegisterBankAccountRequest> BankAccountRequests { get; } = new();

    /// <summary>Estatus con que responde el alta (por defecto <c>Pending</c>, como Pey).</summary>
    public PeyCodiStatus RegisterStatus { get; set; } = PeyCodiStatus.Pending;

    /// <summary>Si no es <c>null</c>, el alta lanza esta excepción.</summary>
    public Exception? RegisterException { get; set; }

    public void Reset()
    {
        BankAccountRequests.Clear();
        RegisterStatus = PeyCodiStatus.Pending;
        RegisterException = null;
    }

    public Task<RegisteredBankAccount> RegisterBankAccountAsync(RegisterBankAccountRequest request, CancellationToken cancellationToken = default)
    {
        BankAccountRequests.Enqueue(request);
        if (RegisterException is { } exception)
        {
            throw exception;
        }

        var id = $"01FAKE{Interlocked.Increment(ref _sequence):D20}";
        return Task.FromResult(new RegisteredBankAccount(id, RegisterStatus));
    }

    public Task<GatewayBankAccount> GetBankAccountAsync(string bankAccountId, CancellationToken cancellationToken = default) =>
        throw NotConfigured();

    public Task<CodiQr> CreateCodiQrAsync(CreateCodiQrRequest request, CancellationToken cancellationToken = default) =>
        throw NotConfigured();

    public Task<CodiPushNotification> CreateCodiPushNotificationAsync(CreateCodiPushNotificationRequest request, CancellationToken cancellationToken = default) =>
        throw NotConfigured();

    public Task<GatewayTransaction> GetTransactionAsync(string transactionId, CancellationToken cancellationToken = default) =>
        throw NotConfigured();

    public Task<GatewayTransactionPage> GetApiKeyTransactionsAsync(string? nextToken = null, CancellationToken cancellationToken = default) =>
        throw NotConfigured();

    public Task<GatewayTransactionPage> GetBankAccountTransactionsAsync(string bankAccountId, string? nextToken = null, CancellationToken cancellationToken = default) =>
        throw NotConfigured();

    public Task<GatewayTransactionPage> GetCodiQrTransactionsAsync(string codiQrId, string? nextToken = null, CancellationToken cancellationToken = default) =>
        throw NotConfigured();

    private static NotSupportedException NotConfigured() => new("FakePaymentGateway: operación aún no simulada.");
}
