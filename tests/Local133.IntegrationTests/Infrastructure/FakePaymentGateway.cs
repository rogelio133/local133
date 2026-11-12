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

    /// <summary>Ids de Pey consultados con <c>GET /bank-account/{id}</c>, en orden.</summary>
    public ConcurrentQueue<string> BankAccountLookups { get; } = new();

    /// <summary>Estatus con que responde la consulta de la cuenta (por defecto <c>Pending</c>).</summary>
    public PeyCodiStatus LookupStatus { get; set; } = PeyCodiStatus.Pending;

    /// <summary><c>errorMessage</c> de la consulta (Pey solo lo manda con <c>Error</c>).</summary>
    public string? LookupErrorMessage { get; set; }

    /// <summary>Si no es <c>null</c>, la consulta lanza esta excepción.</summary>
    public Exception? LookupException { get; set; }

    /// <summary>QRs pedidos con <c>POST /codi/qr</c>, en orden.</summary>
    public ConcurrentQueue<CreateCodiQrRequest> CodiQrRequests { get; } = new();

    /// <summary>Si no es <c>null</c>, la creación del QR lanza esta excepción.</summary>
    public Exception? CodiQrException { get; set; }

    /// <summary>Contenido simulado del QR con el IDC dado (Pey devuelve un JSON de Banxico).</summary>
    public static string QrStringFor(string idc) => $$$"""{"TYP":20,"v":{"DEV":"FAKE"},"ic":{"IDC":"{{{idc}}}","SER":0}}""";

    public void Reset()
    {
        CodiQrRequests.Clear();
        CodiQrException = null;
        BankAccountRequests.Clear();
        RegisterStatus = PeyCodiStatus.Pending;
        RegisterException = null;
        BankAccountLookups.Clear();
        LookupStatus = PeyCodiStatus.Pending;
        LookupErrorMessage = null;
        LookupException = null;
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

    public Task<GatewayBankAccount> GetBankAccountAsync(string bankAccountId, CancellationToken cancellationToken = default)
    {
        BankAccountLookups.Enqueue(bankAccountId);
        if (LookupException is { } exception)
        {
            throw exception;
        }

        return Task.FromResult(new GatewayBankAccount(
            bankAccountId, Alias: null, HolderName: null, Clabe: null, Entity: null, TaxId: null,
            LookupStatus, LookupErrorMessage, CreatedAt: null));
    }

    public Task<CodiQr> CreateCodiQrAsync(CreateCodiQrRequest request, CancellationToken cancellationToken = default)
    {
        CodiQrRequests.Enqueue(request);
        if (CodiQrException is { } exception)
        {
            throw exception;
        }

        var idc = $"IDCFAKE{Interlocked.Increment(ref _sequence):D10}";
        return Task.FromResult(new CodiQr(idc, QrStringFor(idc)));
    }

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
