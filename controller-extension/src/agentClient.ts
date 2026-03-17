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
  CopyFileRequest,
  CopyFileResponse,
  ProcessStatusResponse,
  WaitForExitRequest,
  WaitForExitResponse,
  TerminateProcessRequest,
  StatusResponse,
  ReadTextFileRequest,
  ReadTextFileResponse,
  TailFileRequest,
  TailFileResponse,
  ElementStateRequest,
  ElementStateResponse,
  WaitForElementRequest,
  WaitForElementResponse,
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
   * Start an executable process on the VM.
   * @param request Run parameters (command, optional args, optional workingDir)
   */
  async runExecutable(request: RunRequest): Promise<RunResponse> {
    return this.post<RunResponse>("/run", request);
  }

  /**
   * Get status for a tracked process.
   * @param pid Process ID returned by runExecutable
   */
  async getProcessStatus(pid: number): Promise<ProcessStatusResponse> {
    return this.get<ProcessStatusResponse>(`/process-status?pid=${pid}`);
  }

  /**
   * Wait for a tracked process to exit up to a timeout.
   * @param request Process ID and timeout in milliseconds
   */
  async waitForExit(request: WaitForExitRequest): Promise<WaitForExitResponse> {
    return this.post<WaitForExitResponse>("/wait-for-exit", request);
  }

  /**
   * Terminate a tracked process.
   * @param request Process ID to terminate
   */
  async terminateProcess(request: TerminateProcessRequest): Promise<StatusResponse> {
    return this.post<StatusResponse>("/terminate", request);
  }

  // ─── UI Automation ───────────────────────────────────────────────────────

  /**
   * Retrieve the UI element tree for a running process.
   * @param pid Process ID returned by runExecutable
   */
  async getUiTree(pid: number): Promise<UiTreeResponse> {
    return this.get<UiTreeResponse>(`/ui-tree?pid=${pid}`);
  }

  /**
   * Retrieve the current state snapshot of a UI element.
   */
  async getElementState(request: ElementStateRequest): Promise<ElementStateResponse> {
    return this.post<ElementStateResponse>("/element-state", request);
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
  // ─── File Copy ────────────────────────────────────────────────────────

  /**
   * Copy a file to the target system.
   * @param request File copy parameters (destination, base64 content, overwrite flag)
   */
  async copyFile(request: CopyFileRequest): Promise<CopyFileResponse> {
    return this.post<CopyFileResponse>("/copy-file", request);
  }

  /**
   * Read a UTF-8 text file from the target machine.
   */
  async readTextFile(request: ReadTextFileRequest): Promise<ReadTextFileResponse> {
    return this.post<ReadTextFileResponse>("/read-text-file", request);
  }

  /**
   * Read the last lines from a UTF-8 text file on the target machine.
   */
  async tailFile(request: TailFileRequest): Promise<TailFileResponse> {
    return this.post<TailFileResponse>("/tail-file", request);
  }

  /**
   * Wait for a UI element to appear.
   */
  async waitForElement(request: WaitForElementRequest): Promise<WaitForElementResponse> {
    return this.post<WaitForElementResponse>("/wait-for-element", request);
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
        const err = (await response.clone().json()) as AgentError;
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
