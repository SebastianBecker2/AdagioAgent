import * as vscode from "vscode";
import {
  RunRequest,
  RunResponse,
  UiTreeResponse,
  ScreenshotResponse,
  ClickRequest,
  ClickResponse,
  TypeRequest,
  TypeResponse,
  HealthResponse,
  AgentError,
} from "./schema";

/**
 * HTTP client that wraps all calls to the Windows VM agent REST API.
 */
export class AgentClient {
  private baseUrl: string;

  constructor(baseUrl: string) {
    this.baseUrl = baseUrl.replace(/\/$/, "");
  }

  // ─── Health ──────────────────────────────────────────────────────────────

  async health(): Promise<HealthResponse> {
    return this.get<HealthResponse>("/health");
  }

  // ─── Process ─────────────────────────────────────────────────────────────

  /**
   * Start an installer process on the VM.
   * @param request Run parameters (command, optional args, optional workingDir)
   */
  async runInstaller(request: RunRequest): Promise<RunResponse> {
    return this.post<RunResponse>("/run", request);
  }

  // ─── UI Automation ───────────────────────────────────────────────────────

  /**
   * Retrieve the UI element tree for a running process.
   * @param pid Process ID returned by runInstaller
   */
  async getUiTree(pid: number): Promise<UiTreeResponse> {
    return this.get<UiTreeResponse>(`/ui-tree?pid=${pid}`);
  }

  /**
   * Click a UI element by its element ID.
   * @param pid   Process ID
   * @param elementId  Element ID from the UI tree
   */
  async clickElement(pid: number, elementId: string): Promise<ClickResponse> {
    const request: ClickRequest = { pid, elementId };
    return this.post<ClickResponse>("/click", request);
  }

  /**
   * Type text into a UI element.
   * @param pid       Process ID
   * @param elementId Element ID from the UI tree
   * @param text      Text to type
   */
  async typeText(
    pid: number,
    elementId: string,
    text: string
  ): Promise<TypeResponse> {
    const request: TypeRequest = { pid, elementId, text };
    return this.post<TypeResponse>("/type", request);
  }

  // ─── Screenshot ──────────────────────────────────────────────────────────

  /**
   * Capture a screenshot of the process window.
   * @param pid Process ID
   */
  async getScreenshot(pid: number): Promise<ScreenshotResponse> {
    return this.get<ScreenshotResponse>(`/screenshot?pid=${pid}`);
  }

  // ─── Helpers ─────────────────────────────────────────────────────────────

  private async get<T>(path: string): Promise<T> {
    const url = `${this.baseUrl}${path}`;
    const response = await fetch(url);
    return this.handleResponse<T>(response);
  }

  private async post<T>(path: string, body: unknown): Promise<T> {
    const url = `${this.baseUrl}${path}`;
    const response = await fetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
    return this.handleResponse<T>(response);
  }

  private async handleResponse<T>(response: Response): Promise<T> {
    if (!response.ok) {
      let detail: string | undefined;
      try {
        const err = (await response.json()) as AgentError;
        detail = err.detail ?? err.error;
      } catch {
        detail = await response.text();
      }
      throw new Error(
        `VM agent responded with ${response.status}: ${detail ?? response.statusText}`
      );
    }
    return response.json() as Promise<T>;
  }
}

/**
 * Create an AgentClient using the URL from VS Code configuration.
 */
export function createAgentClient(): AgentClient {
  const config = vscode.workspace.getConfiguration("adagioAgent");
  const url = config.get<string>("vmAgentUrl") ?? "http://localhost:5000";
  return new AgentClient(url);
}
