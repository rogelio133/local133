using System.Collections.Concurrent;
using System.Globalization;

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

    /// <summary>Cobros por celular pedidos con <c>POST /codi/pn</c>, en orden.</summary>
    public ConcurrentQueue<CreateCodiPushNotificationRequest> PushRequests { get; } = new();

    /// <summary>Si no es <c>null</c>, el envío del push lanza esta excepción.</summary>
    public Exception? PushException { get; set; }

    /// <summary>Pagos por IDC para <c>GET /transaction/codi/qr/{idc}</c>; un IDC sin entrada responde una lista vacía.</summary>
    public ConcurrentDictionary<string, IReadOnlyList<GatewayTransaction>> CodiQrTransactions { get; } = new();

    /// <summary>Pagos por página en <c>GET /transaction/codi/qr/{idc}</c> (Pey: 20).</summary>
    public int CodiQrPageSize { get; set; } = 20;

    /// <summary>Consultas de pagos de un QR: IDC y <c>nextToken</c>, en orden.</summary>
    public ConcurrentQueue<(string CodiQrId, string? NextToken)> CodiQrTransactionLookups { get; } = new();

    /// <summary>Transacciones para <c>GET /transaction/{id}</c>; un id sin entrada responde <c>NOT_FOUND</c>.</summary>
    public ConcurrentDictionary<string, GatewayTransaction> Transactions { get; } = new();

    /// <summary>Ids consultados con <c>GET /transaction/{id}</c>, en orden.</summary>
    public ConcurrentQueue<string> TransactionLookups { get; } = new();

    /// <summary>Excepción por IDC o id de transacción: la consulta de ese recurso la lanza.</summary>
    public ConcurrentDictionary<string, Exception> TransactionLookupExceptions { get; } = new();

    /// <summary>Transacción como la devuelve Pey (con la metadata que enviamos al cobrar).</summary>
    public static GatewayTransaction Transaction(
        string id, decimal amount, IReadOnlyDictionary<string, string?> external,
        PeyTransactionStatus status = PeyTransactionStatus.Paid, DateTimeOffset? paidAt = null,
        PeyPayableType payableType = PeyPayableType.ApiCodiQr) =>
        new(id, status, amount, AmountBreakdown: null, "MXN", PeyPaymentMethod.Codi, payableType, PayableId: null,
            "Tacos Don Ramón · 2 tacos al pastor", ApplicationFee: null, CreatedAt: paidAt ?? DateTimeOffset.UtcNow,
            status == PeyTransactionStatus.Paid ? paidAt ?? DateTimeOffset.UtcNow : null,
            new GatewayTransactionMetadata("000000000000000000", 40012, 40, null, "0192191044", external));

    /// <summary>Links de pago pedidos con <c>POST /payment-link/create</c>, en orden.</summary>
    public ConcurrentQueue<CreatePaymentLinkRequest> PaymentLinkRequests { get; } = new();

    /// <summary>Si no es <c>null</c>, la creación del link lanza esta excepción.</summary>
    public Exception? PaymentLinkException { get; set; }

    /// <summary>URL simulada de la página de pago del link con el id dado.</summary>
    public static Uri PaymentLinkUrlFor(string id) => new($"https://pay.example.test/{id}");

    /// <summary>Links para <c>GET /payment-link/{id}</c>; un id sin entrada responde <c>NOT_FOUND</c>.</summary>
    public ConcurrentDictionary<string, GatewayPaymentLink> PaymentLinks { get; } = new();

    public void Reset()
    {
        PaymentLinkRequests.Clear();
        PaymentLinkException = null;
        PaymentLinks.Clear();
        CodiQrTransactions.Clear();
        CodiQrPageSize = 20;
        CodiQrTransactionLookups.Clear();
        Transactions.Clear();
        TransactionLookups.Clear();
        TransactionLookupExceptions.Clear();
        CodiQrRequests.Clear();
        CodiQrException = null;
        PushRequests.Clear();
        PushException = null;
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

    public Task<CodiPushNotification> CreateCodiPushNotificationAsync(CreateCodiPushNotificationRequest request, CancellationToken cancellationToken = default)
    {
        PushRequests.Enqueue(request);
        if (PushException is { } exception)
        {
            throw exception;
        }

        var sequence = Interlocked.Increment(ref _sequence);
        return Task.FromResult(new CodiPushNotification($"01FAKEPN{sequence:D18}", $"{sequence:D20}"));
    }

    public Task<GatewayTransaction> GetTransactionAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        TransactionLookups.Enqueue(transactionId);
        if (TransactionLookupExceptions.TryGetValue(transactionId, out var exception))
        {
            throw exception;
        }

        return Transactions.TryGetValue(transactionId, out var transaction)
            ? Task.FromResult(transaction)
            : throw new PaymentGatewayNotFoundException(
                "Pey: transacción no encontrada.", PeyErrorType.NotFound, 404, "req_fake", "Transaction not found");
    }

    public Task<GatewayTransactionPage> GetApiKeyTransactionsAsync(string? nextToken = null, CancellationToken cancellationToken = default) =>
        throw NotConfigured();

    public Task<GatewayTransactionPage> GetBankAccountTransactionsAsync(string bankAccountId, string? nextToken = null, CancellationToken cancellationToken = default) =>
        throw NotConfigured();

    public Task<GatewayTransactionPage> GetCodiQrTransactionsAsync(string codiQrId, string? nextToken = null, CancellationToken cancellationToken = default)
    {
        CodiQrTransactionLookups.Enqueue((codiQrId, nextToken));
        if (TransactionLookupExceptions.TryGetValue(codiQrId, out var exception))
        {
            throw exception;
        }

        var all = CodiQrTransactions.TryGetValue(codiQrId, out var items) ? items : [];
        var offset = nextToken is null ? 0 : int.Parse(nextToken, CultureInfo.InvariantCulture);
        var page = all.Skip(offset).Take(CodiQrPageSize).ToList();
        var next = offset + CodiQrPageSize < all.Count
            ? (offset + CodiQrPageSize).ToString(CultureInfo.InvariantCulture)
            : null;
        return Task.FromResult(new GatewayTransactionPage(page, next));
    }

    public Task<CreatedPaymentLink> CreatePaymentLinkAsync(CreatePaymentLinkRequest request, CancellationToken cancellationToken = default)
    {
        PaymentLinkRequests.Enqueue(request);
        if (PaymentLinkException is { } exception)
        {
            throw exception;
        }

        var id = $"01FAKEPL{Interlocked.Increment(ref _sequence):D18}";
        return Task.FromResult(new CreatedPaymentLink(id, PaymentLinkUrlFor(id)));
    }

    public Task<GatewayPaymentLink> GetPaymentLinkAsync(string paymentLinkId, CancellationToken cancellationToken = default) =>
        PaymentLinks.TryGetValue(paymentLinkId, out var link)
            ? Task.FromResult(link)
            : throw new PaymentGatewayNotFoundException(
                "Pey: link no encontrado.", PeyErrorType.NotFound, 404, "req_fake", "Payment link not found");

    private static NotSupportedException NotConfigured() => new("FakePaymentGateway: operación aún no simulada.");
}
