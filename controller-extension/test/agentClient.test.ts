import { beforeEach, describe, expect, it, vi } from "vitest";

const { getConfigurationMock } = vi.hoisted(() => ({
  getConfigurationMock: vi.fn(),
}));

vi.mock("vscode", () => ({
  workspace: {
    getConfiguration: getConfigurationMock,
  },
}));

import { AgentClient, AgentClientError, createAgentClient, getCorrelationIdFromError } from "../src/agentClient";

describe("AgentClient", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    (globalThis as { fetch: typeof fetch }).fetch = vi.fn() as unknown as typeof fetch;
  });

  it("normalizes trailing slash in base url", async () => {
    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ status: "healthy", version: "1.0.0" }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      })
    );

    const client = new AgentClient("http://localhost:5000/");
    await client.health();

    expect(fetchMock).toHaveBeenCalledWith("http://localhost:5000/health", {
      headers: {},
    });
  });

  it("calls readiness endpoint for startup validation", async () => {
    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock.mockResolvedValue(
      new Response(
        JSON.stringify({
          status: "ready",
          version: "0.1.0",
          apiVersion: 1,
          platform: "windows",
          uiAutomationAvailable: true,
          issues: [],
        }),
        {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }
      )
    );

    const client = new AgentClient("http://localhost:5000");
    await client.ready();

    expect(fetchMock).toHaveBeenCalledWith("http://localhost:5000/ready", {
      headers: {},
    });
  });

  it("calls diagnostics status endpoint", async () => {
    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock.mockResolvedValue(
      new Response(
        JSON.stringify({
          status: "ready",
          version: "0.1.0",
          apiVersion: 1,
          platform: "windows",
          uiAutomationAvailable: true,
          issues: [],
          runningProcessCount: 0,
          trackedProcessCount: 0,
          timestampUtc: "2026-03-18T00:00:00Z",
        }),
        {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }
      )
    );

    const client = new AgentClient("http://localhost:5000");
    await client.diagnosticsStatus();

    expect(fetchMock).toHaveBeenCalledWith("http://localhost:5000/diagnostics/status", {
      headers: {},
    });
  });

  it("sends POST body for runExecutable", async () => {
    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock.mockResolvedValue(
      new Response(
        JSON.stringify({ pid: 123, status: "running", startedAt: "2026-03-17T00:00:00Z" }),
        {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }
      )
    );

    const client = new AgentClient("http://localhost:5000");
    await client.runExecutable({ command: "C:/Apps/app.exe", arguments: "--foo" });

    expect(fetchMock).toHaveBeenCalledWith(
      "http://localhost:5000/run",
      expect.objectContaining({
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ command: "C:/Apps/app.exe", arguments: "--foo" }),
      })
    );
  });

  it("sends POST body for runInstallerAndCollectArtifacts", async () => {
    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock.mockResolvedValue(
      new Response(
        JSON.stringify({
          pid: 123,
          startedAt: "2026-03-17T00:00:00Z",
          artifacts: {
            exited: true,
            process: {
              pid: 123,
              status: "exited",
              startedAt: "2026-03-17T00:00:00Z",
              exitedAt: "2026-03-17T00:00:10Z",
              exitCode: 0,
            },
            msiEvents: [],
            warnings: [],
          },
        }),
        {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }
      )
    );

    const client = new AgentClient("http://localhost:5000");
    await client.runInstallerAndCollectArtifacts({
      command: "C:/Apps/setup.exe",
      arguments: "/quiet",
      logPath: "C:/Apps/setup.log",
    });

    expect(fetchMock).toHaveBeenCalledWith(
      "http://localhost:5000/run-installer-and-collect-artifacts",
      expect.objectContaining({
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          command: "C:/Apps/setup.exe",
          arguments: "/quiet",
          logPath: "C:/Apps/setup.log",
        }),
      })
    );
  });

  it("sends POST body for runInstallerAndAssert", async () => {
    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock.mockResolvedValue(
      new Response(
        JSON.stringify({
          pid: 123,
          startedAt: "2026-03-17T00:00:00Z",
          artifacts: {
            exited: true,
            process: {
              pid: 123,
              status: "exited",
              startedAt: "2026-03-17T00:00:00Z",
              exitedAt: "2026-03-17T00:00:10Z",
              exitCode: 0,
            },
            msiEvents: [],
            warnings: [],
          },
          assertions: [{ passed: true, message: "Process 123 exited with expected code 0." }],
          passed: true,
        }),
        {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }
      )
    );

    const client = new AgentClient("http://localhost:5000");
    await client.runInstallerAndAssert({
      command: "C:/Apps/setup.exe",
      arguments: "/quiet",
      logPath: "C:/Apps/setup.log",
      expectedExitCode: 0,
      expectedPath: "C:/Program Files/MyApp",
      expectedPathMustBeDirectory: true,
      logMustContainText: "completed",
      logContainsIgnoreCase: true,
    });

    expect(fetchMock).toHaveBeenCalledWith(
      "http://localhost:5000/run-installer-and-assert",
      expect.objectContaining({
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          command: "C:/Apps/setup.exe",
          arguments: "/quiet",
          logPath: "C:/Apps/setup.log",
          expectedExitCode: 0,
          expectedPath: "C:/Program Files/MyApp",
          expectedPathMustBeDirectory: true,
          logMustContainText: "completed",
          logContainsIgnoreCase: true,
        }),
      })
    );
  });

  it("throws with API detail when JSON error response is returned", async () => {
    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ error: "bad", detail: "invalid input" }), {
        status: 400,
        statusText: "Bad Request",
        headers: { "Content-Type": "application/json" },
      })
    );

    const client = new AgentClient("http://localhost:5000");

    await expect(client.health()).rejects.toThrow(
      "VM agent responded with 400: invalid input"
    );
  });

  it("includes correlation ID from error payload when present", async () => {
    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ error: "bad", detail: "invalid input", correlationId: "corr-123" }), {
        status: 400,
        statusText: "Bad Request",
        headers: { "Content-Type": "application/json" },
      })
    );

    const client = new AgentClient("http://localhost:5000");

    await expect(client.health()).rejects.toThrow(
      "VM agent responded with 400: invalid input (Correlation ID: corr-123)"
    );
  });

  it("includes errorCode and remediation hint from structured error payload", async () => {
    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock.mockResolvedValue(
      new Response(
        JSON.stringify({
          error: "bad",
          detail: "path is outside allowlist",
          errorCode: "COMMAND_REJECTED",
          remediationHint: "Use an executable path listed in AgentOptions.AllowedExecutablePaths.",
        }),
        {
          status: 400,
          statusText: "Bad Request",
          headers: { "Content-Type": "application/json" },
        }
      )
    );

    const client = new AgentClient("http://localhost:5000");

    await expect(client.health()).rejects.toThrow(
      "VM agent responded with 400 [COMMAND_REJECTED]: path is outside allowlist Remediation: Use an executable path listed in AgentOptions.AllowedExecutablePaths."
    );
  });

  it("falls back to message field when detail is omitted", async () => {
    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ error: "bad", message: "request payload failed schema validation" }), {
        status: 400,
        statusText: "Bad Request",
        headers: { "Content-Type": "application/json" },
      })
    );

    const client = new AgentClient("http://localhost:5000");

    await expect(client.health()).rejects.toThrow(
      "VM agent responded with 400: request payload failed schema validation"
    );
  });

  it("falls back to X-Correlation-ID response header when body omits correlation ID", async () => {
    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ error: "bad", detail: "invalid input" }), {
        status: 400,
        statusText: "Bad Request",
        headers: { "Content-Type": "application/json", "X-Correlation-ID": "corr-header-7" },
      })
    );

    const client = new AgentClient("http://localhost:5000");

    await expect(client.health()).rejects.toThrow(
      "VM agent responded with 400: invalid input (Correlation ID: corr-header-7)"
    );
  });

  it("exposes correlation ID from AgentClientError helper", () => {
    const err = new AgentClientError(500, "broken", "corr-500");

    expect(getCorrelationIdFromError(err)).toBe("corr-500");
    expect(getCorrelationIdFromError(new Error("oops (Correlation ID: corr-msg-1)"))).toBe("corr-msg-1");
    expect(getCorrelationIdFromError(new Error("no correlation"))).toBeUndefined();
  });

  it("falls back to response text when JSON parsing fails", async () => {
    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock.mockResolvedValue(
      new Response("gateway down", {
        status: 502,
        statusText: "Bad Gateway",
      })
    );

    const client = new AgentClient("http://localhost:5000");

    await expect(client.health()).rejects.toThrow("VM agent responded with 502: gateway down");
  });

  it("createAgentClient uses configured URL or defaults to secure localhost", () => {
    getConfigurationMock.mockReturnValueOnce({
      get: vi.fn((key: string) => {
        if (key === "vmAgentUrl") {
          return "http://remote-agent:7777";
        }

        if (key === "requireHttps") {
          return false;
        }

        return undefined;
      }),
    });

    const configured = createAgentClient() as unknown as { baseUrl: string };
    expect(configured.baseUrl).toBe("http://remote-agent:7777");

    getConfigurationMock.mockReturnValueOnce({
      get: vi.fn().mockReturnValue(undefined),
    });

    const fallback = createAgentClient() as unknown as { baseUrl: string };
    expect(fallback.baseUrl).toBe("https://127.0.0.1:5443/api/v1");
  });

  it("createAgentClient rejects non-https URL when requireHttps is enabled", () => {
    getConfigurationMock.mockReturnValueOnce({
      get: vi.fn((key: string) => {
        if (key === "vmAgentUrl") {
          return "http://remote-agent:7777";
        }

        if (key === "requireHttps") {
          return true;
        }

        return undefined;
      }),
    });

    expect(() => createAgentClient()).toThrow(
      "adagioAgent.vmAgentUrl must use HTTPS when adagioAgent.requireHttps is true."
    );
  });

  it("sends X-API-Key header when configured", async () => {
    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ status: "healthy", version: "1.0.0" }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      })
    );

    const client = new AgentClient("https://127.0.0.1:5443", "secret-key");
    await client.health();

    expect(fetchMock).toHaveBeenCalledWith("https://127.0.0.1:5443/health", {
      headers: {
        "X-API-Key": "secret-key",
      },
    });
  });

  it("createAgentClient forwards configured API key to requests", async () => {
    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ status: "healthy", version: "1.0.0" }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      })
    );

    getConfigurationMock.mockReturnValueOnce({
      get: vi.fn((key: string) => {
        if (key === "vmAgentUrl") {
          return "https://127.0.0.1:5443";
        }

        if (key === "requireHttps") {
          return true;
        }

        if (key === "vmAgentApiKey") {
          return "configured-key";
        }

        return undefined;
      }),
    });

    const client = createAgentClient();
    await client.health();

    expect(fetchMock).toHaveBeenCalledWith("https://127.0.0.1:5443/health", {
      headers: {
        "X-API-Key": "configured-key",
      },
    });
  });

  it("createAgentClient defaults requests to the versioned api base path", async () => {
    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ status: "healthy", version: "1.0.0", apiVersion: 1 }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      })
    );

    getConfigurationMock.mockReturnValueOnce({
      get: vi.fn().mockReturnValue(undefined),
    });

    const client = createAgentClient();
    await client.health();

    expect(fetchMock).toHaveBeenCalledWith("https://127.0.0.1:5443/api/v1/health", {
      headers: {},
    });
  });

  it("calls process lifecycle endpoints with expected paths and payloads", async () => {
    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            pid: 222,
            status: "running",
            startedAt: "2026-03-17T00:00:00Z",
          }),
          { status: 200, headers: { "Content-Type": "application/json" } }
        )
      )
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            exited: true,
            process: {
              pid: 222,
              status: "exited",
              startedAt: "2026-03-17T00:00:00Z",
              exitedAt: "2026-03-17T00:00:05Z",
              exitCode: 0,
            },
          }),
          { status: 200, headers: { "Content-Type": "application/json" } }
        )
      )
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({ status: "ok", message: "Process 222 terminated." }),
          { status: 200, headers: { "Content-Type": "application/json" } }
        )
      );

    const client = new AgentClient("http://localhost:5000");

    await client.getProcessStatus(222);
    await client.waitForExit({ pid: 222, timeoutMilliseconds: 1500 });
    await client.terminateProcess({ pid: 222 });

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      "http://localhost:5000/process-status?pid=222",
      {
        headers: {},
      }
    );
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      "http://localhost:5000/wait-for-exit",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ pid: 222, timeoutMilliseconds: 1500 }),
      })
    );
    expect(fetchMock).toHaveBeenNthCalledWith(
      3,
      "http://localhost:5000/terminate",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ pid: 222 }),
      })
    );
  });

  it("calls collect-install-artifacts endpoint with expected payload", async () => {
    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock.mockResolvedValue(
      new Response(
        JSON.stringify({
          exited: true,
          process: {
            pid: 222,
            status: "exited",
            startedAt: "2026-03-17T00:00:00Z",
            exitedAt: "2026-03-17T00:00:05Z",
            exitCode: 0,
          },
          msiEvents: [],
          warnings: [],
        }),
        { status: 200, headers: { "Content-Type": "application/json" } }
      )
    );

    const client = new AgentClient("http://localhost:5000");
    await client.collectInstallArtifacts({
      pid: 222,
      timeoutMilliseconds: 5000,
      logPath: "C:/Apps/setup.log",
      tailLines: 100,
      includeMsiEvents: true,
      eventEntryCount: 10,
    });

    expect(fetchMock).toHaveBeenCalledWith(
      "http://localhost:5000/collect-install-artifacts",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({
          pid: 222,
          timeoutMilliseconds: 5000,
          logPath: "C:/Apps/setup.log",
          tailLines: 100,
          includeMsiEvents: true,
          eventEntryCount: 10,
        }),
      })
    );
  });

  it("calls generalized workflow endpoints with expected payloads", async () => {
    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            exited: true,
            process: {
              pid: 333,
              status: "exited",
              startedAt: "2026-03-17T00:00:00Z",
              exitedAt: "2026-03-17T00:00:05Z",
              exitCode: 0,
            },
            msiEvents: [],
            warnings: [],
          }),
          { status: 200, headers: { "Content-Type": "application/json" } }
        )
      )
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            pid: 333,
            startedAt: "2026-03-17T00:00:00Z",
            artifacts: {
              exited: true,
              process: {
                pid: 333,
                status: "exited",
                startedAt: "2026-03-17T00:00:00Z",
                exitedAt: "2026-03-17T00:00:05Z",
                exitCode: 0,
              },
              msiEvents: [],
              warnings: [],
            },
          }),
          { status: 200, headers: { "Content-Type": "application/json" } }
        )
      )
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            pid: 333,
            startedAt: "2026-03-17T00:00:00Z",
            artifacts: {
              exited: true,
              process: {
                pid: 333,
                status: "exited",
                startedAt: "2026-03-17T00:00:00Z",
                exitedAt: "2026-03-17T00:00:05Z",
                exitCode: 0,
              },
              msiEvents: [],
              warnings: [],
            },
            assertions: [{ passed: true, message: "Process exited." }],
            passed: true,
          }),
          { status: 200, headers: { "Content-Type": "application/json" } }
        )
      );

    const client = new AgentClient("http://localhost:5000");
    await client.collectProcessArtifacts({ pid: 333, timeoutMilliseconds: 5000 });
    await client.runAndCollectArtifacts({ command: "C:/Apps/app.exe", arguments: "--version" });
    await client.runAndAssert({ command: "C:/Apps/app.exe", expectedExitCode: 0 });

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      "http://localhost:5000/collect-process-artifacts",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ pid: 333, timeoutMilliseconds: 5000 }),
      })
    );
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      "http://localhost:5000/run-and-collect-artifacts",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ command: "C:/Apps/app.exe", arguments: "--version" }),
      })
    );
    expect(fetchMock).toHaveBeenNthCalledWith(
      3,
      "http://localhost:5000/run-and-assert",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ command: "C:/Apps/app.exe", expectedExitCode: 0 }),
      })
    );
  });

  it("calls read-text-file and tail-file endpoints with expected payloads", async () => {
    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({ path: "C:/Apps/setup.log", content: "line1\nline2" }),
          { status: 200, headers: { "Content-Type": "application/json" } }
        )
      )
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({ path: "C:/Apps/setup.log", lines: 50, content: "tail" }),
          { status: 200, headers: { "Content-Type": "application/json" } }
        )
      );

    const client = new AgentClient("http://localhost:5000");

    await client.readTextFile({ path: "C:/Apps/setup.log" });
    await client.tailFile({ path: "C:/Apps/setup.log", lines: 50 });

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      "http://localhost:5000/read-text-file",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ path: "C:/Apps/setup.log" }),
      })
    );
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      "http://localhost:5000/tail-file",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ path: "C:/Apps/setup.log", lines: 50 }),
      })
    );
  });

  it("calls list-directory and file-exists endpoints with expected payloads", async () => {
    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({ path: "C:/Apps", entries: [{ name: "a.txt", path: "C:/Apps/a.txt", isDirectory: false }] }),
          { status: 200, headers: { "Content-Type": "application/json" } }
        )
      )
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({ path: "C:/Apps/a.txt", exists: true, isDirectory: false }),
          { status: 200, headers: { "Content-Type": "application/json" } }
        )
      );

    const client = new AgentClient("http://localhost:5000");

    await client.listDirectory({ path: "C:/Apps" });
    await client.fileExists({ path: "C:/Apps/a.txt" });

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      "http://localhost:5000/list-directory",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ path: "C:/Apps" }),
      })
    );
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      "http://localhost:5000/file-exists",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ path: "C:/Apps/a.txt" }),
      })
    );
  });

  it("calls assertion endpoints with expected payloads", async () => {
    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ passed: true, message: "Process exited." }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        })
      )
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ passed: true, message: "Path exists." }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        })
      )
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ passed: true, message: "Log contains text." }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        })
      );

    const client = new AgentClient("http://localhost:5000");

    await client.assertProcessExited({ pid: 77, timeoutMilliseconds: 5000, expectedExitCode: 0 });
    await client.assertPathExists({ path: "C:/Apps/output", mustBeDirectory: true });
    await client.assertLogContains({ path: "C:/Apps/install.log", containsText: "completed", ignoreCase: true });

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      "http://localhost:5000/assert-process-exited",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ pid: 77, timeoutMilliseconds: 5000, expectedExitCode: 0 }),
      })
    );
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      "http://localhost:5000/assert-path-exists",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ path: "C:/Apps/output", mustBeDirectory: true }),
      })
    );
    expect(fetchMock).toHaveBeenNthCalledWith(
      3,
      "http://localhost:5000/assert-log-contains",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ path: "C:/Apps/install.log", containsText: "completed", ignoreCase: true }),
      })
    );
  });

  it("calls element-state and wait-for-element endpoints with expected payloads", async () => {
    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            id: "button-next",
            type: "button",
            name: "Next",
            automationId: "",
            available: true,
          }),
          { status: 200, headers: { "Content-Type": "application/json" } }
        )
      )
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            found: true,
            element: {
              id: "button-next",
              type: "button",
              name: "Next",
              automationId: "",
              available: true,
            },
          }),
          { status: 200, headers: { "Content-Type": "application/json" } }
        )
      );

    const client = new AgentClient("http://localhost:5000");

    await client.getElementState({ pid: 77, elementId: "button-next" });
    await client.waitForElement({ pid: 77, elementId: "button-next", timeoutMilliseconds: 1000, pollIntervalMilliseconds: 100 });

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      "http://localhost:5000/element-state",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ pid: 77, elementId: "button-next" }),
      })
    );
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      "http://localhost:5000/wait-for-element",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({
          pid: 77,
          elementId: "button-next",
          timeoutMilliseconds: 1000,
          pollIntervalMilliseconds: 100,
        }),
      })
    );
  });

  it("calls focus and send-keys endpoints with expected payloads", async () => {
    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ status: "ok" }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        })
      )
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ status: "ok" }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        })
      );

    const client = new AgentClient("http://localhost:5000");

    await client.setFocus({ pid: 77, elementId: "button-next" });
    await client.sendKeys({ pid: 77, text: "hello" });

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      "http://localhost:5000/focus",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ pid: 77, elementId: "button-next" }),
      })
    );
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      "http://localhost:5000/send-keys",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ pid: 77, text: "hello" }),
      })
    );
  });

  it("calls press-hotkey endpoint with expected payload", async () => {
    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ status: "ok" }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      })
    );

    const client = new AgentClient("http://localhost:5000");
    await client.pressHotkey({ pid: 77, keys: ["alt", "n"] });

    expect(fetchMock).toHaveBeenCalledWith(
      "http://localhost:5000/press-hotkey",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ pid: 77, keys: ["alt", "n"] }),
      })
    );
  });

  it("calls set-checkbox and select-option endpoints with expected payloads", async () => {
    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ status: "ok" }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        })
      )
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ status: "ok" }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        })
      );

    const client = new AgentClient("http://localhost:5000");

    await client.setCheckbox({ pid: 88, elementId: "chk-eula", isChecked: true });
    await client.selectOption({ pid: 88, elementId: "cmb-type", optionText: "Full" });

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      "http://localhost:5000/set-checkbox",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ pid: 88, elementId: "chk-eula", isChecked: true }),
      })
    );
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      "http://localhost:5000/select-option",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ pid: 88, elementId: "cmb-type", optionText: "Full" }),
      })
    );
  });

  it("connectSession posts to /session/connect with optional clientName", async () => {
    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock.mockResolvedValue(
      new Response(
        JSON.stringify({
          sessionId: "abc123",
          createdAtUtc: "2026-03-19T00:00:00Z",
          sessionHeaderName: "X-Adagio-Session-ID",
        }),
        {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }
      )
    );

    const client = new AgentClient("http://localhost:5000");
    await client.connectSession({ clientName: "vscode-adagio-agent" });

    expect(fetchMock).toHaveBeenCalledWith(
      "http://localhost:5000/session/connect",
      expect.objectContaining({
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ clientName: "vscode-adagio-agent" }),
      })
    );
  });

  it("includes X-Adagio-Session-ID header when sessionId is provided", async () => {
    const fetchMock = vi.mocked(globalThis.fetch);
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ status: "healthy", version: "1.0.0" }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      })
    );

    const client = new AgentClient("https://127.0.0.1:5443", "secret-key", "session-42");
    await client.health();

    expect(fetchMock).toHaveBeenCalledWith("https://127.0.0.1:5443/health", {
      headers: {
        "X-API-Key": "secret-key",
        "X-Adagio-Session-ID": "session-42",
      },
    });
  });
});
