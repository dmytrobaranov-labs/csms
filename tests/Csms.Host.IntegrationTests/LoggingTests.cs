using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace Csms.Host.IntegrationTests;

/// <summary>Keeps every log event in memory so tests can inspect what would reach Seq.</summary>
public sealed class CollectingSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> events = new();

    public IReadOnlyCollection<LogEvent> Events => events;

    public void Emit(LogEvent logEvent) => events.Enqueue(logEvent);
}

public sealed partial class LoggingTests
{
    private const string Header = "X-Correlation-ID";

    // /health/live runs no checks, so these tests need no database.
    private const string LivePath = "/health/live";

    [Fact]
    public async Task Missing_correlation_id_is_generated_and_returned()
    {
        await using var factory = CreateFactory(new CollectingSink());

        var response = await factory.CreateClient().GetAsync(LivePath);

        var returned = Assert.Single(response.Headers.GetValues(Header));
        Assert.Matches(ValidId(), returned);
    }

    [Theory]
    [InlineData("abc-123")]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e")]
    [InlineData("Station_7.boot")]
    public async Task Valid_correlation_id_is_echoed_back(string sent)
    {
        await using var factory = CreateFactory(new CollectingSink());

        var response = await SendAsync(factory, sent);

        Assert.Equal(sent, Assert.Single(response.Headers.GetValues(Header)));
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("a/b")]
    [InlineData("<script>")]
    [InlineData("ünïcode")]
    [InlineData("abc\n")] // "$" in a .NET regex would accept a trailing newline; "\z" does not
    public async Task Invalid_correlation_id_is_replaced(string sent)
    {
        await using var factory = CreateFactory(new CollectingSink());

        var response = await SendAsync(factory, sent);

        var returned = Assert.Single(response.Headers.GetValues(Header));
        Assert.NotEqual(sent, returned);
        Assert.Matches(ValidId(), returned);
    }

    [Fact]
    public async Task Too_long_correlation_id_is_replaced()
    {
        await using var factory = CreateFactory(new CollectingSink());
        var sent = new string('a', 65);

        var response = await SendAsync(factory, sent);

        var returned = Assert.Single(response.Headers.GetValues(Header));
        Assert.NotEqual(sent, returned);
        Assert.Matches(ValidId(), returned);
    }

    [Fact]
    public async Task Request_log_event_carries_the_correlation_id()
    {
        var sink = new CollectingSink();
        await using var factory = CreateFactory(sink, minimumLevel: "Debug");

        var response = await SendAsync(factory, "trace-me-42");

        // Every event written while serving the request carries the ID, not just the summary line.
        var duringRequest = sink.Events.Where(e => RequestPathOf(e) == LivePath).ToList();
        Assert.NotEmpty(duringRequest);
        Assert.All(duringRequest, e => Assert.Equal("trace-me-42", Scalar(e, "CorrelationId")));

        // The request completion event ("HTTP GET /health/live responded 200 in ... ms").
        var completed = Assert.Single(duringRequest, IsRequestCompletion);
        Assert.Equal(200, Scalar(completed, "StatusCode"));
        Assert.Equal("trace-me-42", Assert.Single(response.Headers.GetValues(Header)));
    }

    [Theory]
    [InlineData("Debug", true)]
    [InlineData("Information", false)]
    public async Task Minimum_level_comes_from_configuration(string minimumLevel, bool healthRequestLogged)
    {
        var sink = new CollectingSink();
        await using var factory = CreateFactory(sink, minimumLevel);

        await factory.CreateClient().GetAsync(LivePath);

        // Health probes are logged at Debug, so they only show up when configuration allows it.
        Assert.Equal(healthRequestLogged, sink.Events.Any(IsRequestCompletion));
    }

    [Fact]
    public async Task Authorization_header_never_reaches_the_log()
    {
        const string secret = "c2VjcmV0LXBhc3N3b3Jk";
        var sink = new CollectingSink();
        await using var factory = CreateFactory(sink, minimumLevel: "Verbose");
        var request = new HttpRequestMessage(HttpMethod.Get, LivePath);
        request.Headers.TryAddWithoutValidation("Authorization", $"Basic {secret}");

        await factory.CreateClient().SendAsync(request);

        Assert.NotEmpty(sink.Events);
        Assert.DoesNotContain(sink.Events, e =>
            e.RenderMessage().Contains(secret) ||
            e.Properties.Values.Any(v => v.ToString().Contains(secret)));
    }

    private static WebApplicationFactory<Program> CreateFactory(CollectingSink sink, string? minimumLevel = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder
                .UseEnvironment("Testing")
                // Never opened: liveness does not touch the database.
                .UseSetting("ConnectionStrings:Postgres", "Host=127.0.0.1;Database=unused")
                // Serilog picks up every ILogEventSink registered in DI (ReadFrom.Services).
                .ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(sink));

            if (minimumLevel is not null)
            {
                builder.UseSetting("Serilog:MinimumLevel:Default", minimumLevel);
            }
        });

    private static Task<HttpResponseMessage> SendAsync(WebApplicationFactory<Program> factory, string correlationId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, LivePath);
        // Without validation, so the test can send values a careless client might send.
        request.Headers.TryAddWithoutValidation(Header, correlationId);
        return factory.CreateClient().SendAsync(request);
    }

    private static bool IsRequestCompletion(LogEvent e) =>
        RequestPathOf(e) == LivePath && e.Properties.ContainsKey("StatusCode");

    private static string? RequestPathOf(LogEvent e) => Scalar(e, "RequestPath") as string;

    private static object? Scalar(LogEvent e, string name) =>
        e.Properties.TryGetValue(name, out var value) && value is ScalarValue scalar ? scalar.Value : null;

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,64}\z")]
    private static partial Regex ValidId();
}
