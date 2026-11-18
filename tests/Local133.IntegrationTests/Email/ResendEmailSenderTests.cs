using System.Collections.Concurrent;
using System.Text.Json;

using Local133.Infrastructure.Email;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Local133.IntegrationTests.Email;

/// <summary>
/// <see cref="ResendEmailSender"/> (con su pipeline de resiliencia) contra Resend simulado con WireMock.
/// No usa SQL Server ni la red: corre en CI.
/// </summary>
public sealed class ResendEmailSenderTests : IDisposable
{
    private const string ApiKey = "re_test_key";
    private const string From = "Local133 <hola@mail.example.test>";
    private const string Html = """<p><a href="https://local133.test/confirmar?code=SECRETO">Confirmar</a></p>""";
    private const string SentJson = """{"id":"4ef9a417-02e9-4d39-ad75-9611e0fcc33c"}""";

    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly CapturingLoggerProvider _logs = new();
    private readonly ServiceProvider _services;
    private readonly IEmailSender _sender;

    public ResendEmailSenderTests()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Resend:ApiKey"] = ApiKey,
            ["Resend:ApiUrl"] = _server.Url,
            ["Resend:RetryDelay"] = "00:00:00.010",
            ["Resend:MaxRetryAttempts"] = "2",
            ["Email:From"] = From,
        }).Build();

        _services = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(_logs))
            .AddEmail(configuration)
            .BuildServiceProvider();
        _sender = _services.GetRequiredService<IEmailSender>();
    }

    public void Dispose()
    {
        _services.Dispose();
        _server.Stop();
        _server.Dispose();
    }

    private IReadOnlyList<IRequestMessage> Requests =>
        _server.LogEntries.Select(e => e.RequestMessage ?? throw new InvalidOperationException("Entrada sin petición.")).ToList();

    private void Respond(int status, string body, string? state = null, string? nextState = null, (string, string)? header = null)
    {
        var request = _server.Given(Request.Create().WithPath("/emails").UsingPost());
        if (state is not null || nextState is not null)
        {
            request = request.InScenario("envío");
            if (state is not null)
            {
                request = request.WhenStateIs(state);
            }

            if (nextState is not null)
            {
                request = request.WillSetStateTo(nextState);
            }
        }

        var response = Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body);
        if (header is var (name, value))
        {
            response = response.WithHeader(name, value);
        }

        request.RespondWith(response);
    }

    [Fact]
    public async Task Envia_RemitenteDestinatarioAsuntoHtml_ConApiKeyEIdempotencia()
    {
        Respond(200, SentJson);

        await _sender.SendEmailAsync("vendedor@example.test", "Confirma tu correo · Local133", Html);

        var request = Assert.Single(Requests);
        Assert.Equal($"Bearer {ApiKey}", request.Headers!["Authorization"].Single());
        Assert.StartsWith("account-email/", request.Headers["Idempotency-Key"].Single());
        var body = JsonDocument.Parse(request.Body!).RootElement;
        Assert.Equal(From, body.GetProperty("from").GetString());
        Assert.Equal("vendedor@example.test", body.GetProperty("to").EnumerateArray().Single().GetString());
        Assert.Equal("Confirma tu correo · Local133", body.GetProperty("subject").GetString());
        Assert.Equal(Html, body.GetProperty("html").GetString());
        Assert.Contains(_logs.Messages, m => m.Contains("4ef9a417-02e9-4d39-ad75-9611e0fcc33c"));
    }

    [Fact]
    public async Task CadaEnvio_LlevaSuPropiaLlave()
    {
        Respond(200, SentJson);

        await _sender.SendEmailAsync("a@example.test", "Uno", Html);
        await _sender.SendEmailAsync("a@example.test", "Uno", Html);

        var keys = Requests.Select(r => r.Headers!["Idempotency-Key"].Single()).ToList();
        Assert.Equal(2, keys.Distinct().Count());
    }

    [Fact]
    public async Task Error503_SeReintentaConLaMismaLlave()
    {
        Respond(503, """{"statusCode":503,"name":"application_error","message":"Mantenimiento"}""", nextState: "listo");
        Respond(200, SentJson, state: "listo");

        await _sender.SendEmailAsync("a@example.test", "Asunto", Html);

        Assert.Equal(2, Requests.Count);
        Assert.Single(Requests.Select(r => r.Headers!["Idempotency-Key"].Single()).Distinct());
        Assert.DoesNotContain(_logs.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Error429_SeReintentaRespetandoRetryAfter()
    {
        Respond(429, """{"statusCode":429,"name":"rate_limit_exceeded","message":"Too many requests"}""",
            nextState: "listo", header: ("Retry-After", "1"));
        Respond(200, SentJson, state: "listo");

        await _sender.SendEmailAsync("a@example.test", "Asunto", Html);

        Assert.Equal(2, Requests.Count);
    }

    [Fact]
    public async Task Error422_NoSeReintentaNiLanza_YQuedaEnElLog()
    {
        Respond(422, """{"statusCode":422,"name":"validation_error","message":"Invalid `from` field."}""");

        await _sender.SendEmailAsync("a@example.test", "Asunto", Html);

        Assert.Single(Requests);
        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("422"));
    }

    [Fact]
    public async Task FallaPersistente_NoLanza()
    {
        Respond(500, """{"statusCode":500,"name":"internal_server_error","message":"Boom"}""");

        await _sender.SendEmailAsync("a@example.test", "Asunto", Html);

        Assert.Equal(3, Requests.Count); // intento + 2 reintentos
        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task NuncaLoguea_ElCuerpoNiElDestinatario()
    {
        Respond(422, """{"statusCode":422,"name":"validation_error","message":"Invalid"}""");
        await _sender.SendEmailAsync("vendedor@example.test", "Asunto", Html);
        Respond(200, SentJson);
        await _sender.SendEmailAsync("vendedor@example.test", "Asunto", Html);

        Assert.DoesNotContain(_logs.Messages, m => m.Contains("SECRETO") || m.Contains("vendedor@"));
    }

    [Fact]
    public void SinApiKey_GuardaEnDisco()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Resend:ApiKey"] = "",
        }).Build();

        var descriptor = new ServiceCollection().AddEmail(configuration).Single(d => d.ServiceType == typeof(IEmailSender));

        Assert.Equal(typeof(FileEmailSender), descriptor.ImplementationType);
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries => _entries.ToList();

        public IEnumerable<string> Messages => Entries.Select(e => e.Message);

        public ILogger CreateLogger(string categoryName) => new Logger(_entries);

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new LogEntry(logLevel, formatter(state, exception) + (exception is null ? "" : " " + exception)));
        }
    }
}
