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
});
