using System.Text.RegularExpressions;
using Local133.Domain.Webhooks;

namespace Local133.UnitTests.Webhooks;

/// <summary>6.4: el payload de un webhook en el admin, formateado y sin datos sensibles.</summary>
public partial class WebhookPayloadMaskTests
{
    private const string PayerClabe = "012180015826225713";

    [Fact]
    public void Enmascara_la_clabe_del_pagador_en_la_metadata()
    {
        var payload = $$$"""{"type":"codi.qr.payment_received","transactionId":"tx_1","amount":12.5,"metadata":{"clabe":"{{{PayerClabe}}}","bankCode":40012,"trackingKey":"ABC123"}}""";

        var (text, isJson) = WebhookPayloadMask.Format(payload);

        Assert.True(isJson);
        Assert.DoesNotContain(PayerClabe, text);
        Assert.Contains("\"clabe\": \"•••• 5713\"", text);
        Assert.Contains("\"transactionId\": \"tx_1\"", text);
        Assert.Contains("\"trackingKey\": \"ABC123\"", text);
        Assert.Contains("\"bankCode\": 40012", text);
        Assert.Contains("\n", text); // con sangría
    }

    [Fact]
    public void Enmascara_clabe_rfc_y_celular_de_primer_nivel_sin_importar_mayusculas()
    {
        var payload = $$$"""{"type":"bank.account.updated","Clabe":"{{{PayerClabe}}}","taxId":"LOPM800101AB1","phoneNumber":"5512345678","name":"María López"}""";

        var (text, _) = WebhookPayloadMask.Format(payload);

        Assert.DoesNotContain(PayerClabe, text);
        Assert.DoesNotContain("LOPM800101AB1", text);
        Assert.DoesNotContain("5512345678", text);
        Assert.Contains("\"Clabe\": \"•••• 5713\"", text);
        Assert.Contains("\"taxId\": \"•••• AB1\"", text);
        Assert.Contains("\"phoneNumber\": \"•••• 5678\"", text);
        Assert.Contains("María López", text); // acentos legibles
    }

    [Fact]
    public void Enmascara_numeros_largos_sueltos_en_cadenas_numeros_y_arreglos()
    {
        var payload = $$$"""{"description":"Pago desde {{{PayerClabe}}} ok","transactions":[{"account":4152313412345678,"metadata":{"clabe":112180015826225713}}],"numRef":1234567}""";

        var (text, isJson) = WebhookPayloadMask.Format(payload);

        Assert.True(isJson);
        Assert.Empty(LongDigits().Matches(text));
        Assert.Contains("Pago desde •••• 5713 ok", text);
        Assert.Contains("\"account\": \"•••• 5678\"", text);
        Assert.Contains("\"numRef\": 1234567", text); // 7 dígitos no se tocan
    }

    [Fact]
    public void Un_payload_que_no_es_json_enmascara_series_de_diez_digitos_o_mas()
    {
        var (text, isJson) = WebhookPayloadMask.Format($"clabe={PayerClabe}&tel=5512345678&ref=1234567");

        Assert.False(isJson);
        Assert.Equal("clabe=•••• 5713&tel=•••• 5678&ref=1234567", text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Vacio_queda_vacio(string? payload)
    {
        Assert.Equal((string.Empty, false), WebhookPayloadMask.Format(payload));
    }

    [Fact]
    public void Valores_vacios_o_no_numericos_no_truenan()
    {
        var (text, isJson) = WebhookPayloadMask.Format("""{"clabe":"","phone":null,"taxId":true,"items":[null,1,"x"]}""");

        Assert.True(isJson);
        Assert.Contains("\"clabe\": \"\"", text);
        Assert.Contains("\"phone\": null", text);
        Assert.Contains("\"taxId\": true", text);
    }

    [GeneratedRegex(@"\d{16,}")]
    private static partial Regex LongDigits();
}
