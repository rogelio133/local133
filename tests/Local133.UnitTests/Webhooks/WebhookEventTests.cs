using Local133.Domain.Webhooks;

namespace Local133.UnitTests.Webhooks;

/// <summary>Bitácora de webhooks: estatus e idempotencia por webhook-id.</summary>
public class WebhookEventTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 18, 0, 0, TimeSpan.Zero);

    private static WebhookEvent Received() => WebhookEvent.Receive("msg_1", "codi.qr.payment_received", "{}", Now.AddSeconds(-2), Now);

    [Fact]
    public void Nace_recibido_y_sin_intentos()
    {
        var webhook = Received();

        Assert.Equal(WebhookEventStatus.Received, webhook.Status);
        Assert.Equal(0, webhook.Attempts);
        Assert.Equal(Now, webhook.ReceivedAt);
        Assert.Equal(Now.AddSeconds(-2), webhook.WebhookTimestamp);
        Assert.False(webhook.IsSettled);
    }

    [Fact]
    public void Procesado_o_ignorado_ya_no_se_repite()
    {
        var processed = Received();
        processed.MarkProcessed(Now);
        var ignored = Received();
        ignored.MarkIgnored("Fase 5", Now);

        Assert.True(processed.IsSettled);
        Assert.Null(processed.Error);
        Assert.Equal(Now, processed.ProcessedAt);
        Assert.True(ignored.IsSettled);
        Assert.Equal("Fase 5", ignored.Error);
    }

    [Fact]
    public void Fallido_o_reintentable_se_vuelve_a_procesar()
    {
        var failed = Received();
        failed.MarkFailed("venta no encontrada", Now);
        var retry = Received();
        retry.StartAttempt();
        retry.MarkRetryable("timeout");

        Assert.False(failed.IsSettled);
        Assert.Equal(WebhookEventStatus.Failed, failed.Status);
        Assert.False(retry.IsSettled);
        Assert.Equal(WebhookEventStatus.Received, retry.Status);
        Assert.Equal(1, retry.Attempts);
        Assert.Equal("timeout", retry.Error);
    }

    [Fact]
    public void Recorta_el_tipo_y_el_error_largos()
    {
        var webhook = WebhookEvent.Receive("msg_1", new string('x', 200), "{}", Now, Now);
        webhook.MarkFailed(new string('e', 5000), Now);

        Assert.Equal(WebhookEvent.EventTypeMaxLength, webhook.EventType.Length);
        Assert.Equal(WebhookEvent.ErrorMaxLength, webhook.Error!.Length);
    }

    [Fact]
    public void Sin_tipo_queda_vacio() =>
        Assert.Equal("", WebhookEvent.Receive("msg_1", null, "no json", Now, Now).EventType);

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Exige_el_webhook_id(string id) =>
        Assert.Throws<ArgumentException>(() => WebhookEvent.Receive(id, "x", "{}", Now, Now));

    [Fact]
    public void Rechaza_un_webhook_id_demasiado_largo() =>
        Assert.Throws<ArgumentException>(() => WebhookEvent.Receive(new string('a', 101), "x", "{}", Now, Now));
}

/// <summary>6.4: reproceso desde la bitácora del admin.</summary>
public class WebhookEventReprocessTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 18, 0, 0, TimeSpan.Zero);

    private static WebhookEvent Received() => WebhookEvent.Receive("msg_1", "codi.qr.payment_received", "{}", Now, Now);

    [Fact]
    public void Se_reprocesa_todo_salvo_lo_procesado()
    {
        var received = Received();
        var failed = Received();
        failed.MarkFailed("Venta no encontrada", Now);
        var ignored = Received();
        ignored.MarkIgnored("QR fijo", Now);
        var processed = Received();
        processed.MarkProcessed(Now);

        Assert.True(received.CanReprocess);
        Assert.True(failed.CanReprocess);
        Assert.True(ignored.CanReprocess);
        Assert.False(processed.CanReprocess);
    }

    [Fact]
    public void Reprocesar_suma_un_intento()
    {
        var failed = Received();
        failed.StartAttempt();
        failed.MarkFailed("Venta no encontrada", Now);

        failed.StartReprocess();

        Assert.Equal(2, failed.Attempts);
    }

    [Fact]
    public void Un_procesado_no_se_reprocesa()
    {
        var processed = Received();
        processed.MarkProcessed(Now);

        Assert.Throws<InvalidOperationException>(processed.StartReprocess);
    }
}
