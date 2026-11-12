using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Local133.Application.Payments;
using Local133.Domain.Pey;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace Local133.Infrastructure.Pey;

/// <summary>
/// Cliente HTTP de la API de Pey (<c>https://api.pey.mx</c>). Los reintentos y timeouts los pone el pipeline
/// de resiliencia registrado en <see cref="PeyServiceCollectionExtensions.AddPeyGateway"/>.
/// </summary>
internal sealed class PeyClient(HttpClient http, IOptions<PeyOptions> options, ILogger<PeyClient> logger) : IPaymentGateway
{
    public async Task<RegisteredBankAccount> RegisterBankAccountAsync(
        RegisterBankAccountRequest request, CancellationToken cancellationToken = default)
    {
        PeyRequestGuard.Validate(request);
        var body = new PeyRegisterBankAccountBody(request.Alias.Trim(), request.HolderName.Trim(), request.Clabe, request.Entity);
        var response = await SendAsync<PeyRegisterBankAccountResponse>(HttpMethod.Post, "bank-account", body, cancellationToken);
        return new RegisteredBankAccount(Required(response.Id, "id"), Required(response.CodiStatus, "codiStatus"));
    }

    public async Task<GatewayBankAccount> GetBankAccountAsync(string bankAccountId, CancellationToken cancellationToken = default)
    {
        PeyRequestGuard.RequireId(bankAccountId, nameof(bankAccountId));
        var response = await SendAsync<PeyBankAccountResponse>(
            HttpMethod.Get, $"bank-account/{Uri.EscapeDataString(bankAccountId)}", body: null, cancellationToken);
        return new GatewayBankAccount(
            Required(response.Id, "id"),
            response.Alias,
            response.Name,
            response.Clabe,
            response.Entity,
            response.TaxId,
            Required(response.CodiStatus, "codiStatus"),
            response.ErrorMessage,
            FromUnix(response.DateCreated));
    }

    public async Task<CodiQr> CreateCodiQrAsync(CreateCodiQrRequest request, CancellationToken cancellationToken = default)
    {
        PeyRequestGuard.Validate(request);
        var body = new PeyCreateQrBody(
            request.Amount,
            request.Concept,
            request.ExpirationSeconds,
            FormatNumRef(request.NumRef),
            GenerateImage: false, // el QR se dibuja en cliente a partir de qrString
            request.BankAccountId,
            request.Metadata);
        var response = await SendAsync<PeyCreateQrResponse>(HttpMethod.Post, "codi/qr", body, cancellationToken);
        return new CodiQr(Required(response.Id, "id"), Required(response.QrString, "qrString"));
    }

    public async Task<CodiPushNotification> CreateCodiPushNotificationAsync(
        CreateCodiPushNotificationRequest request, CancellationToken cancellationToken = default)
    {
        PeyRequestGuard.Validate(request);
        var body = new PeyCreatePushBody(
            request.Amount,
            request.PhoneNumber,
            request.Concept,
            request.ExpirationSeconds,
            FormatNumRef(request.NumRef),
            request.BankAccountId,
            request.Metadata);
        var response = await SendAsync<PeyCreatePushResponse>(HttpMethod.Post, "codi/pn", body, cancellationToken);
        return new CodiPushNotification(Required(response.TransactionId, "transactionId"), Required(response.CodiId, "codiId"));
    }

    public async Task<GatewayTransaction> GetTransactionAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        PeyRequestGuard.RequireId(transactionId, nameof(transactionId));
        var response = await SendAsync<PeyTransactionResponse>(
            HttpMethod.Get, $"transaction/{Uri.EscapeDataString(transactionId)}", body: null, cancellationToken);
        return ToTransaction(response);
    }

    public Task<GatewayTransactionPage> GetApiKeyTransactionsAsync(string? nextToken = null, CancellationToken cancellationToken = default) =>
        GetPageAsync("transaction/api-key", nextToken, cancellationToken);

    public Task<GatewayTransactionPage> GetBankAccountTransactionsAsync(
        string bankAccountId, string? nextToken = null, CancellationToken cancellationToken = default)
    {
        PeyRequestGuard.RequireId(bankAccountId, nameof(bankAccountId));
        return GetPageAsync($"transaction/bank-account/{Uri.EscapeDataString(bankAccountId)}", nextToken, cancellationToken);
    }

    public Task<GatewayTransactionPage> GetCodiQrTransactionsAsync(
        string codiQrId, string? nextToken = null, CancellationToken cancellationToken = default)
    {
        PeyRequestGuard.RequireId(codiQrId, nameof(codiQrId));
        return GetPageAsync($"transaction/codi/qr/{Uri.EscapeDataString(codiQrId)}", nextToken, cancellationToken);
    }

    private async Task<GatewayTransactionPage> GetPageAsync(string path, string? nextToken, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(nextToken))
        {
            path += $"?nextToken={Uri.EscapeDataString(nextToken)}";
        }

        var response = await SendAsync<PeyTransactionPageResponse>(HttpMethod.Get, path, body: null, cancellationToken);
        var items = (response.Items ?? []).Select(ToTransaction).ToList();
        return new GatewayTransactionPage(items, string.IsNullOrEmpty(response.NextToken) ? null : response.NextToken);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
        where T : class
    {
        EnsureConfigured();
        var logPath = "/" + path.Split('?', 2)[0];

        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            // StringContent (no JsonContent): lleva Content-Length y se puede reenviar en un reintento.
            request.Content = new StringContent(
                JsonSerializer.Serialize(body, body.GetType(), PeyJson.Options), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or ExecutionRejectedException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(ex, "No se pudo comunicar con Pey en {Method} {Path}", method, logPath);
            throw new PaymentGatewayUnavailableException(
                $"No se pudo comunicar con Pey ({method} {logPath}): {ex.Message}", innerException: ex);
        }

        using (response)
        {
            var statusCode = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadErrorAsync(response, cancellationToken);
                var type = PeyJson.ParseErrorType(error?.Error?.Type);
                var gatewayMessage = error?.Error?.Message ?? error?.Message;
                var requestId = error?.Error?.RequestId;
                logger.LogWarning(
                    "Pey respondió {StatusCode} ({ErrorType}) a {Method} {Path}; requestId {RequestId}: {GatewayMessage}",
                    statusCode, error?.Error?.Type, method, logPath, requestId, gatewayMessage);
                throw PeyErrorMapper.Map(statusCode, type, requestId, gatewayMessage);
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(PeyJson.Options, cancellationToken)
                    ?? throw new JsonException("Cuerpo vacío.");
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Respuesta inesperada de Pey ({StatusCode}) a {Method} {Path}", statusCode, method, logPath);
                throw new PaymentGatewayException(
                    $"Respuesta inesperada de Pey ({method} {logPath}): {ex.Message}", statusCode: statusCode, innerException: ex);
            }
        }
    }

    private static async Task<PeyErrorEnvelope?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(raw) ? null : JsonSerializer.Deserialize<PeyErrorEnvelope>(raw, PeyJson.Options);
        }
        catch (JsonException)
        {
            return null; // p. ej. una página HTML de un 502: se clasifica por código HTTP.
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(options.Value.ApiKey))
        {
            throw new InvalidOperationException(
                $"Falta la API key de Pey ('{PeyOptions.SectionName}:{nameof(PeyOptions.ApiKey)}'). Configúrala con " +
                $"dotnet user-secrets set \"{PeyOptions.SectionName}:{nameof(PeyOptions.ApiKey)}\" \"<api-key>\" --project src/Local133.Web");
        }
    }

    private static GatewayTransaction ToTransaction(PeyTransactionResponse response) => new(
        Required(response.Id, "id"),
        Required(response.Status, "status"),
        Required(response.Amount, "amount"),
        response.AmountBreakdown is { } b ? new GatewayAmountBreakdown(b.Base, b.Tip, b.Fee) : null,
        response.Currency,
        response.PaymentMethod,
        response.PayableType,
        response.PayableId,
        response.Description,
        response.ApplicationFee,
        FromUnix(response.DateCreated),
        FromUnix(response.DatePaid),
        ToMetadata(response.Metadata));

    private static GatewayTransactionMetadata ToMetadata(PeyTransactionMetadata? metadata) => metadata is null
        ? GatewayTransactionMetadata.Empty
        : new GatewayTransactionMetadata(
            metadata.Clabe,
            metadata.BankCode,
            metadata.AccountType,
            metadata.Description,
            metadata.TrackingKey,
            metadata.ExternalMetadata?.ToDictionary(p => p.Key, p => PeyJson.ScalarToString(p.Value))
                ?? new Dictionary<string, string?>());

    private static string? FormatNumRef(int? numRef) => numRef?.ToString(CultureInfo.InvariantCulture);

    private static DateTimeOffset? FromUnix(long? seconds) =>
        seconds is { } s ? DateTimeOffset.FromUnixTimeSeconds(s) : null;

    private static string Required(string? value, string field) =>
        string.IsNullOrEmpty(value) ? throw MissingField(field) : value;

    private static T Required<T>(T? value, string field) where T : struct =>
        value ?? throw MissingField(field);

    private static PaymentGatewayException MissingField(string field) =>
        new($"Respuesta inesperada de Pey: falta '{field}'.");
}
