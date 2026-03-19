using System.Net;
using System.Text;
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

    [Fact]
    public async Task Ready_VersionedRoute_ReturnsSameResponseAsDirectRoute()
    {
        var direct = await _client.GetAsync("/ready");
        var versioned = await _client.GetAsync("/api/v1/ready");

        Assert.Equal(HttpStatusCode.OK, direct.StatusCode);
        Assert.Equal(HttpStatusCode.OK, versioned.StatusCode);

        var directPayload = await ReadJson<ReadinessResponse>(direct);
        var versionedPayload = await ReadJson<ReadinessResponse>(versioned);

        Assert.Equal(directPayload.Status, versionedPayload.Status);
        Assert.Equal(directPayload.Version, versionedPayload.Version);
        Assert.Equal(directPayload.ApiVersion, versionedPayload.ApiVersion);
        Assert.Equal(directPayload.Platform, versionedPayload.Platform);
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
        Assert.Equal(AgentErrorCodes.ValidationFailed, versionedPayload.ErrorCode);
        Assert.Equal(versionedPayload.ErrorCode, legacyPayload.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(versionedPayload.RemediationHint));
        Assert.False(string.IsNullOrWhiteSpace(legacyPayload.RemediationHint));
    }

    [Fact]
    public async Task ReadTextFile_MissingPath_ReturnsPathNotFoundCode()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"adagio-missing-{Guid.NewGuid():N}.txt");
        var body = JsonSerializer.Serialize(new { path = missingPath });
        var request = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/read-text-file", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var payload = await ReadJson<ErrorResponse>(response);
        Assert.Equal(AgentErrorCodes.PathNotFound, payload.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(payload.RemediationHint));
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
        Assert.Equal(AgentErrorCodes.ProcessNotFound, versionedPayload.ErrorCode);
        Assert.Equal(versionedPayload.ErrorCode, legacyPayload.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(versionedPayload.RemediationHint));
        Assert.False(string.IsNullOrWhiteSpace(legacyPayload.RemediationHint));
    }

    [Fact]
    public async Task VersionedAndLegacyRoute_CollectInstallArtifacts_ReturnIdenticalValidationErrors()
    {
        using var versionedBody = new StringContent("{\"pid\":0,\"timeoutMilliseconds\":5000}", Encoding.UTF8, "application/json");
        using var legacyBody = new StringContent("{\"pid\":0,\"timeoutMilliseconds\":5000}", Encoding.UTF8, "application/json");

        var versionedResponse = await _client.PostAsync("/api/v1/collect-install-artifacts", versionedBody);
        var legacyResponse = await _client.PostAsync("/collect-install-artifacts", legacyBody);

        Assert.Equal(HttpStatusCode.BadRequest, versionedResponse.StatusCode);
        Assert.Equal(versionedResponse.StatusCode, legacyResponse.StatusCode);

        var versionedPayload = await ReadJson<ErrorResponse>(versionedResponse);
        var legacyPayload = await ReadJson<ErrorResponse>(legacyResponse);
        Assert.Equal(versionedPayload.Error, legacyPayload.Error);
        Assert.Equal(AgentErrorCodes.ValidationFailed, versionedPayload.ErrorCode);
        Assert.Equal(versionedPayload.ErrorCode, legacyPayload.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(versionedPayload.RemediationHint));
        Assert.False(string.IsNullOrWhiteSpace(legacyPayload.RemediationHint));
    }

    [Fact]
    public async Task VersionedAndLegacyRoute_CollectInstallArtifacts_ReturnIdenticalProcessNotFoundErrors()
    {
        using var versionedBody = new StringContent("{\"pid\":999999,\"timeoutMilliseconds\":5000}", Encoding.UTF8, "application/json");
        using var legacyBody = new StringContent("{\"pid\":999999,\"timeoutMilliseconds\":5000}", Encoding.UTF8, "application/json");

        var versionedResponse = await _client.PostAsync("/api/v1/collect-install-artifacts", versionedBody);
        var legacyResponse = await _client.PostAsync("/collect-install-artifacts", legacyBody);

        Assert.Equal(HttpStatusCode.NotFound, versionedResponse.StatusCode);
        Assert.Equal(versionedResponse.StatusCode, legacyResponse.StatusCode);

        var versionedPayload = await ReadJson<ErrorResponse>(versionedResponse);
        var legacyPayload = await ReadJson<ErrorResponse>(legacyResponse);
        Assert.Equal(versionedPayload.Error, legacyPayload.Error);
        Assert.Equal(AgentErrorCodes.ProcessNotFound, versionedPayload.ErrorCode);
        Assert.Equal(versionedPayload.ErrorCode, legacyPayload.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(versionedPayload.RemediationHint));
        Assert.False(string.IsNullOrWhiteSpace(legacyPayload.RemediationHint));
    }

    [Fact]
    public async Task VersionedAndLegacyRoute_RunInstallerAndCollectArtifacts_ReturnIdenticalValidationErrors()
    {
        using var versionedBody = new StringContent("{\"command\":\"\"}", Encoding.UTF8, "application/json");
        using var legacyBody = new StringContent("{\"command\":\"\"}", Encoding.UTF8, "application/json");

        var versionedResponse = await _client.PostAsync("/api/v1/run-installer-and-collect-artifacts", versionedBody);
        var legacyResponse = await _client.PostAsync("/run-installer-and-collect-artifacts", legacyBody);

        Assert.Equal(HttpStatusCode.BadRequest, versionedResponse.StatusCode);
        Assert.Equal(versionedResponse.StatusCode, legacyResponse.StatusCode);

        var versionedPayload = await ReadJson<ErrorResponse>(versionedResponse);
        var legacyPayload = await ReadJson<ErrorResponse>(legacyResponse);
        Assert.Equal(versionedPayload.Error, legacyPayload.Error);
        Assert.Equal(AgentErrorCodes.ValidationFailed, versionedPayload.ErrorCode);
        Assert.Equal(versionedPayload.ErrorCode, legacyPayload.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(versionedPayload.RemediationHint));
        Assert.False(string.IsNullOrWhiteSpace(legacyPayload.RemediationHint));
    }

    [Fact]
    public async Task VersionedAndLegacyRoute_RunInstallerAndAssert_ReturnIdenticalValidationErrors()
    {
        using var versionedBody = new StringContent("{\"command\":\"\"}", Encoding.UTF8, "application/json");
        using var legacyBody = new StringContent("{\"command\":\"\"}", Encoding.UTF8, "application/json");

        var versionedResponse = await _client.PostAsync("/api/v1/run-installer-and-assert", versionedBody);
        var legacyResponse = await _client.PostAsync("/run-installer-and-assert", legacyBody);

        Assert.Equal(HttpStatusCode.BadRequest, versionedResponse.StatusCode);
        Assert.Equal(versionedResponse.StatusCode, legacyResponse.StatusCode);

        var versionedPayload = await ReadJson<ErrorResponse>(versionedResponse);
        var legacyPayload = await ReadJson<ErrorResponse>(legacyResponse);
        Assert.Equal(versionedPayload.Error, legacyPayload.Error);
        Assert.Equal(AgentErrorCodes.ValidationFailed, versionedPayload.ErrorCode);
        Assert.Equal(versionedPayload.ErrorCode, legacyPayload.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(versionedPayload.RemediationHint));
        Assert.False(string.IsNullOrWhiteSpace(legacyPayload.RemediationHint));
    }

    [Fact]
    public async Task VersionedAndLegacyRoute_ReadTextFile_ReturnIdenticalPathNotFoundErrors()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"adagio-missing-read-{Guid.NewGuid():N}.txt");
        var body = JsonSerializer.Serialize(new { path = missingPath });
        using var versionedRequest = new StringContent(body, Encoding.UTF8, "application/json");
        using var legacyRequest = new StringContent(body, Encoding.UTF8, "application/json");

        var versionedResponse = await _client.PostAsync("/api/v1/read-text-file", versionedRequest);
        var legacyResponse = await _client.PostAsync("/read-text-file", legacyRequest);

        await AssertStructuredErrorParity(
            versionedResponse,
            legacyResponse,
            HttpStatusCode.NotFound,
            AgentErrorCodes.PathNotFound);
    }

    [Fact]
    public async Task VersionedAndLegacyRoute_TailFile_ReturnIdenticalPathNotFoundErrors()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"adagio-missing-tail-{Guid.NewGuid():N}.log");
        var body = JsonSerializer.Serialize(new { path = missingPath, lines = 10 });
        using var versionedRequest = new StringContent(body, Encoding.UTF8, "application/json");
        using var legacyRequest = new StringContent(body, Encoding.UTF8, "application/json");

        var versionedResponse = await _client.PostAsync("/api/v1/tail-file", versionedRequest);
        var legacyResponse = await _client.PostAsync("/tail-file", legacyRequest);

        await AssertStructuredErrorParity(
            versionedResponse,
            legacyResponse,
            HttpStatusCode.NotFound,
            AgentErrorCodes.PathNotFound);
    }

    [Fact]
    public async Task VersionedAndLegacyRoute_ListDirectory_ReturnIdenticalPathNotFoundErrors()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"adagio-missing-dir-{Guid.NewGuid():N}");
        var body = JsonSerializer.Serialize(new { path = missingPath });
        using var versionedRequest = new StringContent(body, Encoding.UTF8, "application/json");
        using var legacyRequest = new StringContent(body, Encoding.UTF8, "application/json");

        var versionedResponse = await _client.PostAsync("/api/v1/list-directory", versionedRequest);
        var legacyResponse = await _client.PostAsync("/list-directory", legacyRequest);

        await AssertStructuredErrorParity(
            versionedResponse,
            legacyResponse,
            HttpStatusCode.NotFound,
            AgentErrorCodes.PathNotFound);
    }

    [Fact]
    public async Task VersionedAndLegacyRoute_AssertProcessExited_ReturnIdenticalProcessNotFoundErrors()
    {
        var body = JsonSerializer.Serialize(new { pid = 999999, timeoutMilliseconds = 5000 });
        using var versionedRequest = new StringContent(body, Encoding.UTF8, "application/json");
        using var legacyRequest = new StringContent(body, Encoding.UTF8, "application/json");

        var versionedResponse = await _client.PostAsync("/api/v1/assert-process-exited", versionedRequest);
        var legacyResponse = await _client.PostAsync("/assert-process-exited", legacyRequest);

        await AssertStructuredErrorParity(
            versionedResponse,
            legacyResponse,
            HttpStatusCode.NotFound,
            AgentErrorCodes.ProcessNotFound);
    }

    [Fact]
    public async Task VersionedAndLegacyRoute_AssertPathExists_ReturnIdenticalValidationErrors()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"adagio-missing-assert-{Guid.NewGuid():N}");
        var body = JsonSerializer.Serialize(new { path = missingPath, mustBeDirectory = true });
        using var versionedRequest = new StringContent(body, Encoding.UTF8, "application/json");
        using var legacyRequest = new StringContent(body, Encoding.UTF8, "application/json");

        var versionedResponse = await _client.PostAsync("/api/v1/assert-path-exists", versionedRequest);
        var legacyResponse = await _client.PostAsync("/assert-path-exists", legacyRequest);

        await AssertStructuredErrorParity(
            versionedResponse,
            legacyResponse,
            HttpStatusCode.BadRequest,
            AgentErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task VersionedAndLegacyRoute_AssertLogContains_ReturnIdenticalPathNotFoundErrors()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"adagio-missing-log-{Guid.NewGuid():N}.log");
        var body = JsonSerializer.Serialize(new { path = missingPath, containsText = "done", ignoreCase = true });
        using var versionedRequest = new StringContent(body, Encoding.UTF8, "application/json");
        using var legacyRequest = new StringContent(body, Encoding.UTF8, "application/json");

        var versionedResponse = await _client.PostAsync("/api/v1/assert-log-contains", versionedRequest);
        var legacyResponse = await _client.PostAsync("/assert-log-contains", legacyRequest);

        await AssertStructuredErrorParity(
            versionedResponse,
            legacyResponse,
            HttpStatusCode.NotFound,
            AgentErrorCodes.PathNotFound);
    }

    [Fact]
    public async Task DiagnosticsStatus_VersionedRoute_ReturnsSameResponseAsDirectRoute()
    {
        var direct = await _client.GetAsync("/diagnostics/status");
        var versioned = await _client.GetAsync("/api/v1/diagnostics/status");

        Assert.Equal(HttpStatusCode.OK, direct.StatusCode);
        Assert.Equal(HttpStatusCode.OK, versioned.StatusCode);

        var directPayload = await ReadJson<DiagnosticsStatusResponse>(direct);
        var versionedPayload = await ReadJson<DiagnosticsStatusResponse>(versioned);

        Assert.Equal(directPayload.Status, versionedPayload.Status);
        Assert.Equal(directPayload.Version, versionedPayload.Version);
        Assert.Equal(directPayload.ApiVersion, versionedPayload.ApiVersion);
        Assert.Equal(directPayload.Platform, versionedPayload.Platform);
        Assert.Equal(directPayload.RunningProcessCount, versionedPayload.RunningProcessCount);
    }

    [Fact]
    public async Task DiagnosticsExportMetadata_VersionedRoute_ReturnsSameResponseAsDirectRoute()
    {
        var direct = await _client.GetAsync("/diagnostics/export-metadata");
        var versioned = await _client.GetAsync("/api/v1/diagnostics/export-metadata");

        Assert.Equal(HttpStatusCode.OK, direct.StatusCode);
        Assert.Equal(HttpStatusCode.OK, versioned.StatusCode);

        var directPayload = await ReadJson<SupportBundleMetadataResponse>(direct);
        var versionedPayload = await ReadJson<SupportBundleMetadataResponse>(versioned);

        Assert.Equal(directPayload.Version, versionedPayload.Version);
        Assert.Equal(directPayload.ApiVersion, versionedPayload.ApiVersion);
        Assert.Equal(directPayload.Platform, versionedPayload.Platform);
        Assert.Equal(directPayload.ReadinessStatus, versionedPayload.ReadinessStatus);
        Assert.Equal(directPayload.ApiKeyHeaderName, versionedPayload.ApiKeyHeaderName);
    }

    [Fact]
    public async Task SwaggerJson_IsAvailableAndDeclaresVersionedServer()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.TryGetProperty("openapi", out _));
        Assert.True(document.RootElement.TryGetProperty("servers", out var servers));

        var hasVersionedServer = servers
            .EnumerateArray()
            .Any(server =>
                server.TryGetProperty("url", out var url) &&
                string.Equals(url.GetString(), "/api/v1", StringComparison.Ordinal));

        Assert.True(hasVersionedServer);
    }

    [Fact]
    public async Task CorrelationId_Header_IsEchoedInResponse()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Correlation-ID", "test-correlation-123");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var values));
        Assert.Contains("test-correlation-123", values);
    }

    [Fact]
    public async Task ValidationFailure_ReturnsStandardizedErrorResponseWithCorrelationId()
    {
        var response = await _client.GetAsync("/process-status?pid=not-a-number");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var headerValues));
        var correlationHeader = headerValues.Single();

        var payload = await ReadJson<ErrorResponse>(response);
        Assert.Equal("Request validation failed.", payload.Error);
        Assert.Equal(payload.Error, payload.Message);
        Assert.Equal(AgentErrorCodes.ValidationFailed, payload.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(payload.RemediationHint));
        Assert.False(string.IsNullOrWhiteSpace(payload.CorrelationId));
        Assert.Equal(correlationHeader, payload.CorrelationId);
    }

    [Fact]
    public async Task MissingApiKey_ReturnsStandardizedUnauthorizedErrorWithCorrelationId()
    {
        using var factory = new AgentFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SecurityOptions:RequireApiKey"] = "true",
                    ["SecurityOptions:ApiKey"] = "integration-test-key",
                    ["SecurityOptions:RequireHttps"] = "false",
                });
            });
        });

        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var headerValues));
        var correlationHeader = headerValues.Single();

        var payload = await ReadJson<ErrorResponse>(response);
        Assert.Contains("Missing required header", payload.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(payload.Error, payload.Message);
        Assert.Equal(AgentErrorCodes.Unauthorized, payload.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(payload.RemediationHint));
        Assert.Equal(correlationHeader, payload.CorrelationId);
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

    private static async Task AssertStructuredErrorParity(
        HttpResponseMessage versionedResponse,
        HttpResponseMessage legacyResponse,
        HttpStatusCode expectedStatusCode,
        string expectedErrorCode)
    {
        Assert.Equal(expectedStatusCode, versionedResponse.StatusCode);
        Assert.Equal(versionedResponse.StatusCode, legacyResponse.StatusCode);

        var versionedPayload = await ReadJson<ErrorResponse>(versionedResponse);
        var legacyPayload = await ReadJson<ErrorResponse>(legacyResponse);
        Assert.Equal(versionedPayload.Error, legacyPayload.Error);
        Assert.Equal(expectedErrorCode, versionedPayload.ErrorCode);
        Assert.Equal(versionedPayload.ErrorCode, legacyPayload.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(versionedPayload.RemediationHint));
        Assert.False(string.IsNullOrWhiteSpace(legacyPayload.RemediationHint));
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
                    ["Urls"] = "http://127.0.0.1:5000",
                    ["SecurityOptions:RequireHttps"] = "false",
                    ["AgentOptions:AllowedExecutablePaths:0"] = Path.GetTempPath(),
                    ["AgentOptions:AllowedWritablePaths:0"] = Path.GetTempPath(),
                    ["AgentOptions:AllowedReadablePaths:0"] = Path.GetTempPath(),
                });
            });
        }
    }
}
