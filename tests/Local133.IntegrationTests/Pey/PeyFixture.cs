using System.Text.Json;

using Local133.Application.Payments;
using Local133.Infrastructure.Pey;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WireMock;
using WireMock.Server;

namespace Local133.IntegrationTests.Pey;

/// <summary>
/// Pey simulado con WireMock + <see cref="IPaymentGateway"/> real (con su pipeline de resiliencia) apuntando a él.
/// No usa SQL Server ni la red: corre en CI.
/// </summary>
public sealed class PeyFixture : IDisposable
{
    public const string ApiKey = "test-key";

    private readonly ServiceProvider _services;

    public PeyFixture(string? apiKey = ApiKey, TimeSpan? attemptTimeout = null)
    {
        Server = WireMockServer.Start();
        var settings = new Dictionary<string, string?>
        {
            ["Pey:BaseUrl"] = Server.Url,
            ["Pey:ApiKey"] = apiKey,
            ["Pey:RetryDelay"] = "00:00:00.010",
            ["Pey:MaxRetryAttempts"] = "2",
        };
        if (attemptTimeout is { } timeout)
        {
            settings["Pey:AttemptTimeout"] = timeout.ToString("c");
            settings["Pey:TotalTimeout"] = (timeout * 10).ToString("c");
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddLogging().AddPeyGateway(configuration);
        services.ConfigureHttpClientDefaults(http => http.AddHttpMessageHandler(() => new RecordingHandler(SentUris)));
        _services = services.BuildServiceProvider();
        Gateway = _services.GetRequiredService<IPaymentGateway>();
    }

    public WireMockServer Server { get; }

    /// <summary>URIs tal como salieron del <c>HttpClient</c> (WireMock solo muestra la ruta ya decodificada).</summary>
    public List<string> SentUris { get; } = [];

    public IPaymentGateway Gateway { get; }

    /// <summary>Peticiones que recibió el Pey simulado, en orden.</summary>
    public IReadOnlyList<IRequestMessage> Requests =>
        Server.LogEntries.Select(e => e.RequestMessage ?? throw new InvalidOperationException("Entrada sin petición.")).ToList();

    public IRequestMessage SingleRequest() => Assert.Single(Requests);

    public static JsonElement BodyOf(IRequestMessage request)
    {
        Assert.NotNull(request.Body);
        return JsonDocument.Parse(request.Body).RootElement;
    }

    public void Dispose()
    {
        _services.Dispose();
        Server.Stop();
        Server.Dispose();
    }

    private sealed class RecordingHandler(List<string> uris) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (uris)
            {
                uris.Add(request.RequestUri!.AbsoluteUri);
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
