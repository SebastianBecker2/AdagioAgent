using System.Net;
using System.Text.Json;
using AdagioMachineAgent.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AdagioMachineAgent.Tests;

/// <summary>
/// Integration tests that verify the versioning middleware and health contract
/// using a real in-process Kestrel host via WebApplicationFactory.
/// </summary>
public sealed class VersioningIntegrationTests : IClassFixture<VersioningIntegrationTests.AgentFactory>
{
    private readonly HttpClient _client;

    public VersioningIntegrationTests(AgentFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ── /health ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Health_DirectRoute_ReturnsOkWithVersionFields()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await ReadJson<HealthResponse>(response);
        Assert.Equal("healthy", payload.Status);
        Assert.False(string.IsNullOrWhiteSpace(payload.Version));
        Assert.Equal(1, payload.ApiVersion);
    }

    [Fact]
    public async Task Health_VersionedRoute_ReturnsSameResponseAsDirectRoute()
    {
        var direct = await _client.GetAsync("/health");
        var versioned = await _client.GetAsync("/api/v1/health");

        Assert.Equal(HttpStatusCode.OK, direct.StatusCode);
        Assert.Equal(HttpStatusCode.OK, versioned.StatusCode);

        var directPayload = await ReadJson<HealthResponse>(direct);
        var versionedPayload = await ReadJson<HealthResponse>(versioned);

        Assert.Equal(directPayload.Status, versionedPayload.Status);
        Assert.Equal(directPayload.Version, versionedPayload.Version);
        Assert.Equal(directPayload.ApiVersion, versionedPayload.ApiVersion);
    }

    [Fact]
    public async Task Health_VersionedRoute_ContainsApiVersionField()
    {
        var response = await _client.GetAsync("/api/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await ReadJson<HealthResponse>(response);
        Assert.Equal(1, payload.ApiVersion);
        Assert.False(string.IsNullOrWhiteSpace(payload.MinSupportedClientVersion));
    }

    // ── versioned routing for non-health endpoints ────────────────────────

    [Fact]
    public async Task VersionedRoute_Run_RoutesCorrectlyToUnderlyingEndpoint()
    {
        // Send an intentionally invalid body so the controller returns 400 – we only
        // care that the request was routed to the correct endpoint, not that it
        // succeeded.
        using var body = new StringContent("{\"command\":\"\"}", System.Text.Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/v1/run", body);

        // 400 means the routing worked and the controller validated the input.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await ReadJson<ErrorResponse>(response);
        Assert.Equal("Command is required.", payload.Error);
    }

    [Fact]
    public async Task LegacyRoute_Run_StillResponds()
    {
        using var body = new StringContent("{\"command\":\"\"}", System.Text.Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/run", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await ReadJson<ErrorResponse>(response);
        Assert.Equal("Command is required.", payload.Error);
    }

    [Fact]
    public async Task VersionedAndLegacyRoute_ReturnIdenticalErrors()
    {
        using var bodyA = new StringContent("{\"command\":\"\"}", System.Text.Encoding.UTF8, "application/json");
        using var bodyB = new StringContent("{\"command\":\"\"}", System.Text.Encoding.UTF8, "application/json");

        var versionedResponse = await _client.PostAsync("/api/v1/run", bodyA);
        var legacyResponse = await _client.PostAsync("/run", bodyB);

        Assert.Equal(versionedResponse.StatusCode, legacyResponse.StatusCode);

        var versionedPayload = await ReadJson<ErrorResponse>(versionedResponse);
        var legacyPayload = await ReadJson<ErrorResponse>(legacyResponse);
        Assert.Equal(versionedPayload.Error, legacyPayload.Error);
    }

    [Fact]
    public async Task VersionedAndLegacyRoute_ProcessStatus_ReturnIdenticalNotFoundErrors()
    {
        var versionedResponse = await _client.GetAsync("/api/v1/process-status?pid=999999");
        var legacyResponse = await _client.GetAsync("/process-status?pid=999999");

        Assert.Equal(HttpStatusCode.NotFound, versionedResponse.StatusCode);
        Assert.Equal(versionedResponse.StatusCode, legacyResponse.StatusCode);

        var versionedPayload = await ReadJson<ErrorResponse>(versionedResponse);
        var legacyPayload = await ReadJson<ErrorResponse>(legacyResponse);
        Assert.Equal(versionedPayload.Error, legacyPayload.Error);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static async Task<T> ReadJson<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
        Assert.NotNull(result);
        return result;
    }

    // ── test factory ──────────────────────────────────────────────────────

    public sealed class AgentFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Set environment to "Testing" so ASP.NET Core loads
            // appsettings.Testing.json, which disables HTTPS and API-key
            // requirements for the in-process test server.
            builder.UseEnvironment("Testing");

            // Override AllowedExecutablePaths so process-control endpoints
            // accept paths under the system temp directory during tests.
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AgentOptions:AllowedExecutablePaths:0"] = Path.GetTempPath(),
                    ["AgentOptions:AllowedWritablePaths:0"] = Path.GetTempPath(),
                    ["AgentOptions:AllowedReadablePaths:0"] = Path.GetTempPath(),
                });
            });
        }
    }
}
