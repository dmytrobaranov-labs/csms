using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace Csms.Host.IntegrationTests;

/// <summary>Starts one PostgreSQL container shared by the tests that need a reachable database.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    // Same image as docker-compose.yml, so tests run against the version we develop on.
    public PostgreSqlContainer Container { get; } = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public Task InitializeAsync() => Container.StartAsync();

    public Task DisposeAsync() => Container.DisposeAsync().AsTask();
}

public sealed class HealthEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    [InlineData("/health/live")]
    public async Task Returns_healthy_when_database_is_reachable(string path)
    {
        await using var factory = CreateFactory(postgres.Container.GetConnectionString());

        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    public async Task Readiness_returns_unhealthy_when_database_is_unreachable(string path)
    {
        await using var factory = CreateFactory(UnreachableConnectionString());

        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Liveness_returns_healthy_when_database_is_unreachable()
    {
        await using var factory = CreateFactory(UnreachableConnectionString());

        var response = await factory.CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Startup_fails_when_connection_string_is_missing()
    {
        await using var factory = CreateFactory(connectionString: "");

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ConnectionStrings:Postgres", error.Message);
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            // Not "Development": that environment loads user-secrets, and the owner's
            // local connection string must not leak into the tests.
            .UseEnvironment("Testing")
            .UseSetting("ConnectionStrings:Postgres", connectionString));

    /// <summary>
    /// A connection string to a localhost port with nothing listening on it, so the
    /// connection is refused immediately instead of waiting for a timeout.
    /// </summary>
    private static string UnreachableConnectionString()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return $"Host=127.0.0.1;Port={port};Database=csms;Username=csms;Password=unused;Timeout=2";
    }
}
