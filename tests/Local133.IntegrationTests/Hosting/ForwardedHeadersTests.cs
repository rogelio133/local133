using System.Net;
using Local133.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Local133.IntegrationTests.Hosting;

/// <summary>
/// Detrás de dev tunnels (o del proxy de producción) la petición llega por HTTP con <c>X-Forwarded-Proto: https</c>:
/// el sitio no debe redirigirla a su puerto HTTPS interno, o el webhook de Pey recibe un 307 inservible.
/// </summary>
public class ForwardedHeadersTests(Local133WebFactory factory) : WebTestBase(factory)
{
    private const string RemoteIpHeader = "X-Test-Remote-Ip";

    [Fact]
    public async Task Sin_proxy_una_peticion_HTTP_se_redirige_a_HTTPS()
    {
        var response = await PostWebhookAsync(remoteIp: "127.0.0.1", forwardedProto: null);

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("https://localhost:7180/api/webhooks/pey", response.Headers.Location?.ToString());
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public async Task Desde_el_tunel_local_con_X_Forwarded_Proto_https_llega_al_webhook(string remoteIp)
    {
        var response = await PostWebhookAsync(remoteIp, forwardedProto: "https");

        // Llegó al endpoint: sin firma ⇒ 401 (no 307).
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Un_proxy_no_confiable_no_puede_cambiar_el_esquema()
    {
        var response = await PostWebhookAsync(remoteIp: "203.0.113.7", forwardedProto: "https");

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(string remoteIp, string? forwardedProto)
    {
        using var site = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // Como el perfil https: hay puerto HTTPS, así que UseHttpsRedirection redirige lo que llega por HTTP.
            services.Configure<HttpsRedirectionOptions>(o => o.HttpsPort = 7180);
            services.AddTransient<IStartupFilter, RemoteIpStartupFilter>();
        }));
        var client = site.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://localhost"),
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/pey")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(RemoteIpHeader, remoteIp);
        if (forwardedProto is not null)
        {
            request.Headers.Add("X-Forwarded-Proto", forwardedProto);
            request.Headers.Add("X-Forwarded-For", "198.51.100.20");
        }

        return await client.SendAsync(request);
    }

    /// <summary>TestServer no tiene IP remota: la fija desde un header, antes del pipeline del sitio.</summary>
    private sealed class RemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(RemoteIpHeader, out var ip))
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse(ip.ToString());
                }

                await nextMiddleware(context);
            });
            next(app);
        };
    }
}
