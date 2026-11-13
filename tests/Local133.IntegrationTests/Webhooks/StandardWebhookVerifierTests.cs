using Local133.Infrastructure.Webhooks;

namespace Local133.IntegrationTests.Webhooks;

/// <summary>Firmas Standard Webhooks (las de Pey). Sin SQL Server: corren en CI.</summary>
public class StandardWebhookVerifierTests
{
    // Vector de prueba oficial de Standard Webhooks (github.com/standard-webhooks/standard-webhooks).
    private const string Secret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw";
    private const string Id = "msg_p5jXN8AQM9LWM0D4loKWxJek";
    private const string Timestamp = "1614265330";
    private const string Body = "{\"test\": 2432232314}";
    private const string ExpectedSignature = "v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=";

    private static readonly DateTimeOffset SentAt = DateTimeOffset.FromUnixTimeSeconds(1614265330);

    [Fact]
    public void Calcula_la_firma_del_vector_oficial() =>
        Assert.Equal(ExpectedSignature, StandardWebhookVerifier.ComputeSignature(Secret, Id, Timestamp, Body));

    [Fact]
    public void Acepta_la_firma_valida_y_devuelve_el_timestamp()
    {
        var result = StandardWebhookVerifier.Verify(Secret, Id, Timestamp, ExpectedSignature, Body, SentAt.AddSeconds(30));

        Assert.True(result.IsValid);
        Assert.Equal(SentAt, result.Timestamp);
    }

    [Fact]
    public void Acepta_el_secreto_sin_prefijo_whsec() =>
        Assert.True(StandardWebhookVerifier.Verify(Secret["whsec_".Length..], Id, Timestamp, ExpectedSignature, Body, SentAt).IsValid);

    [Fact]
    public void Acepta_si_alguna_de_varias_firmas_coincide()
    {
        var header = $"v1,Zm9vYmFyYmF6 v2,{ExpectedSignature[3..]} {ExpectedSignature}";

        Assert.True(StandardWebhookVerifier.Verify(Secret, Id, Timestamp, header, Body, SentAt).IsValid);
    }

    [Theory]
    [InlineData("{\"test\": 2432232315}")]
    [InlineData("{\"test\":2432232314}")]
    [InlineData("{\"test\": 2432232314} ")]
    public void Rechaza_el_body_alterado(string body) =>
        Assert.Equal(WebhookSignatureStatus.InvalidSignature,
            StandardWebhookVerifier.Verify(Secret, Id, Timestamp, ExpectedSignature, body, SentAt).Status);

    [Fact]
    public void Rechaza_otro_webhook_id() =>
        Assert.Equal(WebhookSignatureStatus.InvalidSignature,
            StandardWebhookVerifier.Verify(Secret, "msg_otro", Timestamp, ExpectedSignature, Body, SentAt).Status);

    [Fact]
    public void Rechaza_otro_secreto() =>
        Assert.Equal(WebhookSignatureStatus.InvalidSignature,
            StandardWebhookVerifier.Verify("whsec_" + Convert.ToBase64String(new byte[24]), Id, Timestamp, ExpectedSignature, Body, SentAt).Status);

    [Theory]
    [InlineData("v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OA=")] // un bit distinto
    [InlineData("v2,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=")] // otra versión
    [InlineData("g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=")]    // sin versión
    [InlineData("v1,no-es-base64")]
    [InlineData("v1,")]
    public void Rechaza_firmas_invalidas(string header) =>
        Assert.Equal(WebhookSignatureStatus.InvalidSignature,
            StandardWebhookVerifier.Verify(Secret, Id, Timestamp, header, Body, SentAt).Status);

    [Theory]
    [InlineData(-301)]
    [InlineData(301)]
    [InlineData(-86_400)]
    public void Rechaza_el_timestamp_fuera_de_5_minutos(int offsetSeconds) =>
        Assert.Equal(WebhookSignatureStatus.InvalidTimestamp,
            StandardWebhookVerifier.Verify(Secret, Id, Timestamp, ExpectedSignature, Body, SentAt.AddSeconds(-offsetSeconds)).Status);

    [Theory]
    [InlineData(-300)]
    [InlineData(300)]
    public void Acepta_el_timestamp_en_el_limite_de_tolerancia(int offsetSeconds) =>
        Assert.True(StandardWebhookVerifier.Verify(Secret, Id, Timestamp, ExpectedSignature, Body, SentAt.AddSeconds(offsetSeconds)).IsValid);

    [Theory]
    [InlineData("abc")]
    [InlineData("-1614265330")]
    [InlineData("1614265330.5")]
    [InlineData("99999999999999999999")]
    public void Rechaza_timestamps_que_no_son_segundos_unix(string timestamp) =>
        Assert.Equal(WebhookSignatureStatus.InvalidTimestamp,
            StandardWebhookVerifier.Verify(Secret, Id, timestamp, ExpectedSignature, Body, SentAt).Status);

    [Theory]
    [InlineData(null, Timestamp, ExpectedSignature)]
    [InlineData(Id, null, ExpectedSignature)]
    [InlineData(Id, Timestamp, null)]
    [InlineData("", Timestamp, ExpectedSignature)]
    [InlineData(Id, " ", ExpectedSignature)]
    public void Sin_headers_no_es_valido(string? id, string? timestamp, string? signature) =>
        Assert.Equal(WebhookSignatureStatus.MissingHeaders,
            StandardWebhookVerifier.Verify(Secret, id, timestamp, signature, Body, SentAt).Status);

    [Fact]
    public void Un_secreto_mal_formado_es_un_error_de_configuracion() =>
        Assert.Throws<InvalidOperationException>(() =>
            StandardWebhookVerifier.Verify("whsec_%%%", Id, Timestamp, ExpectedSignature, Body, SentAt));
}
