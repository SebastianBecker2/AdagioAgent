using System.Net;
using System.Text;
using System.Text.Json;
using AdagioMachineAgent.Models;
using AdagioMachineAgent.Services;
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

    [Fact]
    public void Startup_Fails_WhenInstallerSchemaVersionIsUnsupported()
    {
        var options = new InstallerConfigOptions
        {
            SchemaVersion = InstallerConfigOptions.MaxSupportedSchemaVersion + 1,
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            InstallerConfigCompatibilityPolicy.Validate(options));
        Assert.Contains("InstallerConfig.SchemaVersion", ex.Message, StringComparison.Ordinal);
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
    public async Task SessionConnect_VersionedRoute_ReturnsSameContractAsDirectRoute()
    {
        using var directBody = new StringContent("{}", Encoding.UTF8, "application/json");
        using var versionedBody = new StringContent("{}", Encoding.UTF8, "application/json");

        var direct = await _client.PostAsync("/session/connect", directBody);
        var versioned = await _client.PostAsync("/api/v1/session/connect", versionedBody);

        Assert.Equal(HttpStatusCode.OK, direct.StatusCode);
        Assert.Equal(HttpStatusCode.OK, versioned.StatusCode);

        var directPayload = await ReadJson<ConnectSessionResponse>(direct);
        var versionedPayload = await ReadJson<ConnectSessionResponse>(versioned);

        Assert.False(string.IsNullOrWhiteSpace(directPayload.SessionId));
        Assert.False(string.IsNullOrWhiteSpace(versionedPayload.SessionId));
        Assert.Equal(SessionService.SessionHeaderName, directPayload.SessionHeaderName);
        Assert.Equal(directPayload.SessionHeaderName, versionedPayload.SessionHeaderName);
    }

    [Fact]
    public async Task SessionConnect_Returns503WhenSessionLimitIsReached()
    {
        using var factory = new AgentFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SecurityOptions:RequireHttps"] = "false",
                    ["AgentOptions:MaxConcurrentSessions"] = "2",
                });
            });
        });

        using var client = factory.CreateClient();

        // Fill the session cap.
        for (var i = 0; i < 2; i++)
        {
            using var body = new StringContent("{}", Encoding.UTF8, "application/json");
            var ok = await client.PostAsync("/session/connect", body);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        // One more should be refused.
        using var extraBody = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/v1/session/connect", extraBody);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var payload = await ReadJson<ErrorResponse>(response);
        Assert.Equal(AgentErrorCodes.AgentBusy, payload.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(payload.RemediationHint));
    }

    [Fact]
    public async Task ProcessStatus_ReturnsSessionNotFoundForUnknownExplicitSession()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/process-status?pid=999999");
        request.Headers.Add(SessionService.SessionHeaderName, "missing-session");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var payload = await ReadJson<ErrorResponse>(response);
        Assert.Equal(AgentErrorCodes.SessionNotFound, payload.ErrorCode);
        Assert.Contains("missing-session", payload.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessStatus_IsScopedToOwningSessionAcrossVersionedAndLegacyRoutes()
    {
        var commandInfo = ResolveLongRunningCommand();
        var sessionA = await ConnectSession("/session/connect");
        var sessionB = await ConnectSession("/api/v1/session/connect");

        var runPayload = await StartTrackedProcess(commandInfo, sessionA.SessionId, "/run");

        try
        {
            using var ownerRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/process-status?pid={runPayload.Pid}");
            ownerRequest.Headers.Add(SessionService.SessionHeaderName, sessionA.SessionId);

            var ownerResponse = await _client.SendAsync(ownerRequest);

            Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);
            var ownerPayload = await ReadJson<ProcessStatusResponse>(ownerResponse);
            Assert.Equal(runPayload.Pid, ownerPayload.Pid);

            using var otherSessionRequest = new HttpRequestMessage(HttpMethod.Get, $"/process-status?pid={runPayload.Pid}");
            otherSessionRequest.Headers.Add(SessionService.SessionHeaderName, sessionB.SessionId);

            var otherSessionResponse = await _client.SendAsync(otherSessionRequest);

            Assert.Equal(HttpStatusCode.NotFound, otherSessionResponse.StatusCode);
            var otherSessionPayload = await ReadJson<ErrorResponse>(otherSessionResponse);
            Assert.Equal(AgentErrorCodes.ProcessNotFound, otherSessionPayload.ErrorCode);
        }
        finally
        {
            using var terminateBody = new StringContent(
                JsonSerializer.Serialize(new { pid = runPayload.Pid }),
                Encoding.UTF8,
                "application/json");
            using var terminateRequest = new HttpRequestMessage(HttpMethod.Post, "/terminate")
            {
                Content = terminateBody,
            };
            terminateRequest.Headers.Add(SessionService.SessionHeaderName, sessionA.SessionId);
            await _client.SendAsync(terminateRequest);
        }
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
    public async Task DiagnosticsStatus_IncludesSessionFields()
    {
        var response = await _client.GetAsync("/diagnostics/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await ReadJson<DiagnosticsStatusResponse>(response);
        Assert.True(payload.ActiveSessionCount >= 1);
        // No non-legacy sessions created by other tests => oldest age is null.
        // The JSON deserializer will set it to null if the field is absent or null.
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

    private async Task<ConnectSessionResponse> ConnectSession(string route)
    {
        using var body = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync(route, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJson<ConnectSessionResponse>(response);
    }

    private async Task<RunResponse> StartTrackedProcess(
        (string Command, string? Arguments) commandInfo,
        string sessionId,
        string route)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    command = commandInfo.Command,
                    arguments = commandInfo.Arguments,
                }),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Add(SessionService.SessionHeaderName, sessionId);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJson<RunResponse>(response);
    }

    private static (string Command, string? Arguments) ResolveLongRunningCommand()
    {
        if (OperatingSystem.IsWindows())
        {
            var command = Path.Combine(Environment.SystemDirectory, "ping.exe");
            return (command, "127.0.0.1 -n 20");
        }

        return ("/bin/sleep", "20");
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
                var allowedExecutableRoots = new List<string> { Path.GetTempPath() };
                if (OperatingSystem.IsWindows())
                {
                    allowedExecutableRoots.Add(Environment.SystemDirectory);
                }
                else
                {
                    allowedExecutableRoots.Add("/bin");
                }

                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Urls"] = "http://127.0.0.1:5000",
                    ["SecurityOptions:RequireHttps"] = "false",
                    ["AgentOptions:AllowedExecutablePaths:0"] = allowedExecutableRoots[0],
                    ["AgentOptions:AllowedExecutablePaths:1"] = allowedExecutableRoots.Count > 1 ? allowedExecutableRoots[1] : null,
                    ["AgentOptions:AllowedWritablePaths:0"] = Path.GetTempPath(),
                    ["AgentOptions:AllowedReadablePaths:0"] = Path.GetTempPath(),
                });
            });
        }
    }
}
