import { beforeEach, describe, expect, it, vi } from "vitest";

const { getConfigurationMock } = vi.hoisted(() => ({
  getConfigurationMock: vi.fn(),
}));

vi.mock("vscode", () => ({
  workspace: {
    getConfiguration: getConfigurationMock,
  },
}));

import { AgentClient, createAgentClient } from "../src/agentClient";

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

    expect(fetchMock).toHaveBeenCalledWith("http://localhost:5000/health");
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

  it("createAgentClient uses configured URL or defaults to localhost", () => {
    getConfigurationMock.mockReturnValueOnce({
      get: vi.fn().mockReturnValue("http://remote-agent:7777"),
    });

    const configured = createAgentClient() as unknown as { baseUrl: string };
    expect(configured.baseUrl).toBe("http://remote-agent:7777");

    getConfigurationMock.mockReturnValueOnce({
      get: vi.fn().mockReturnValue(undefined),
    });

    const fallback = createAgentClient() as unknown as { baseUrl: string };
    expect(fallback.baseUrl).toBe("http://localhost:5000");
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
      "http://localhost:5000/process-status?pid=222"
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
});
