using System.Text;
using Local133.Application.Webhooks;
using Local133.Infrastructure.Pey;
using Local133.Infrastructure.Webhooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Local133.Web.Controllers;

/// <summary>
/// Webhook único de Pey (<c>POST /api/webhooks/pey</c>, un webhook por API key). Lee el body crudo → verifica la
/// firma Standard Webhooks → lo guarda y procesa en la misma petición. 200 si quedó resuelto (procesado, repetido,
/// ignorado o fallido sin remedio); 500 si conviene que Pey lo reintente; 401 si la firma no es válida.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/webhooks/pey")]
[ApiExplorerSettings(IgnoreApi = true)]
public class PeyWebhooksController(
    IPeyWebhookProcessor processor,
    IOptionsMonitor<PeyOptions> options,
    TimeProvider clock,
    ILogger<PeyWebhooksController> logger) : ControllerBase
{
    /// <summary>Los payloads de Pey son de unos cientos de bytes.</summary>
    public const int MaxBodyBytes = 64 * 1024;

    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        var secret = options.CurrentValue.WebhookSecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            logger.LogError("Llegó un webhook de Pey pero falta Pey:WebhookSecret; se responde 500 para que Pey lo reintente");
            return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Webhook sin configurar.");
        }

        // El body se lee tal cual: la firma es sobre los bytes recibidos, no sobre un JSON re-serializado.
        string body;
        using (var reader = new StreamReader(Request.Body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)))
        {
            body = await reader.ReadToEndAsync(cancellationToken);
        }

        var webhookId = Header(StandardWebhookVerifier.IdHeader);
        var signature = StandardWebhookVerifier.Verify(
            secret, webhookId, Header(StandardWebhookVerifier.TimestampHeader), Header(StandardWebhookVerifier.SignatureHeader),
            body, clock.GetUtcNow());
        if (!signature.IsValid)
        {
            logger.LogWarning("Webhook rechazado ({Status}); webhook-id {WebhookId}", signature.Status, webhookId);
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Firma inválida.");
        }

        var result = await processor.HandleAsync(webhookId!, signature.Timestamp, body, cancellationToken);
        return result == WebhookHandlingResult.RetryLater
            ? Problem(statusCode: StatusCodes.Status500InternalServerError, title: "No se pudo procesar; reintenta.")
            : Ok(new { status = result.ToString() });
    }

    private string? Header(string name) => Request.Headers.TryGetValue(name, out var value) ? value.ToString() : null;
}
