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
  ReadinessResponse,
  DiagnosticsStatusResponse,
  AgentError,
  CopyFileRequest,
  CopyFileResponse,
  ProcessStatusResponse,
  WaitForExitRequest,
  WaitForExitResponse,
  CollectInstallArtifactsRequest,
  CollectInstallArtifactsResponse,
  CollectProcessArtifactsRequest,
  CollectProcessArtifactsResponse,
  RunInstallerAndCollectArtifactsRequest,
  RunInstallerAndCollectArtifactsResponse,
  RunAndCollectArtifactsRequest,
  RunAndCollectArtifactsResponse,
  RunInstallerAndAssertRequest,
  RunInstallerAndAssertResponse,
  RunAndAssertRequest,
  RunAndAssertResponse,
  TerminateProcessRequest,
  StatusResponse,
  ReadTextFileRequest,
  ReadTextFileResponse,
  TailFileRequest,
  TailFileResponse,
  ListDirectoryRequest,
  ListDirectoryResponse,
  FileExistsRequest,
  FileExistsResponse,
  AssertProcessExitedRequest,
  AssertPathExistsRequest,
  AssertLogContainsRequest,
  AssertionResponse,
  ElementStateRequest,
  ElementStateResponse,
  WaitForElementRequest,
  WaitForElementResponse,
  SetFocusRequest,
  SendKeysRequest,
  PressHotkeyRequest,
  SetCheckboxRequest,
  SelectOptionRequest,
} from "./schema";

export class AgentClientError extends Error {
  readonly status: number;
  readonly detail?: string;
  readonly correlationId?: string;
  readonly errorCode?: string;
  readonly remediationHint?: string;

  constructor(
    status: number,
    detail?: string,
    correlationId?: string,
    errorCode?: string,
    remediationHint?: string
  ) {
    const errorCodeSuffix = errorCode ? ` [${errorCode}]` : "";
    const remediationSuffix = remediationHint ? ` Remediation: ${remediationHint}` : "";
    const correlationSuffix = correlationId ? ` (Correlation ID: ${correlationId})` : "";
    super(
      `VM agent responded with ${status}${errorCodeSuffix}: ${detail ?? "Unexpected error"}${remediationSuffix}${correlationSuffix}`
    );
    this.name = "AgentClientError";
    this.status = status;
    this.detail = detail;
    this.correlationId = correlationId;
    this.errorCode = errorCode;
    this.remediationHint = remediationHint;
  }
}

export function getCorrelationIdFromError(error: unknown): string | undefined {
  if (error instanceof AgentClientError) {
    return error.correlationId;
  }

  if (error instanceof Error) {
    const match = error.message.match(/Correlation ID:\s*([^\)\s]+)/i);
    return match?.[1];
  }

  return undefined;
}

/**
 * HTTP client that wraps all calls to the Windows VM agent REST API.
 */
export class AgentClient {
  private baseUrl: string;
  private apiKey?: string;

  constructor(baseUrl: string, apiKey?: string) {
    this.baseUrl = baseUrl.replace(/\/$/, "");
    this.apiKey = apiKey;
  }

  // ─── Health ──────────────────────────────────────────────────────────────

  async health(): Promise<HealthResponse> {
    return this.get<HealthResponse>("/health");
  }

  async ready(): Promise<ReadinessResponse> {
    return this.get<ReadinessResponse>("/ready");
  }

  async diagnosticsStatus(): Promise<DiagnosticsStatusResponse> {
    return this.get<DiagnosticsStatusResponse>("/diagnostics/status");
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
   * Wait for a process and collect diagnostics such as log tail and optional event data.
   */
  async collectInstallArtifacts(
    request: CollectInstallArtifactsRequest
  ): Promise<CollectInstallArtifactsResponse> {
    return this.post<CollectInstallArtifactsResponse>("/collect-install-artifacts", request);
  }

  /**
   * Wait for a process and collect diagnostics such as log tail and optional event data.
   */
  async collectProcessArtifacts(
    request: CollectProcessArtifactsRequest
  ): Promise<CollectProcessArtifactsResponse> {
    return this.post<CollectProcessArtifactsResponse>("/collect-process-artifacts", request);
  }

  /**
   * Start an installer process and collect diagnostics when it exits or times out.
   */
  async runInstallerAndCollectArtifacts(
    request: RunInstallerAndCollectArtifactsRequest
  ): Promise<RunInstallerAndCollectArtifactsResponse> {
    return this.post<RunInstallerAndCollectArtifactsResponse>(
      "/run-installer-and-collect-artifacts",
      request
    );
  }

  /**
   * Start a process and collect diagnostics when it exits or times out.
   */
  async runAndCollectArtifacts(
    request: RunAndCollectArtifactsRequest
  ): Promise<RunAndCollectArtifactsResponse> {
    return this.post<RunAndCollectArtifactsResponse>(
      "/run-and-collect-artifacts",
      request
    );
  }

  /**
   * Start an installer process, collect diagnostics, and evaluate workflow assertions.
   */
  async runInstallerAndAssert(
    request: RunInstallerAndAssertRequest
  ): Promise<RunInstallerAndAssertResponse> {
    return this.post<RunInstallerAndAssertResponse>(
      "/run-installer-and-assert",
      request
    );
  }

  /**
   * Start a process, collect diagnostics, and evaluate workflow assertions.
   */
  async runAndAssert(
    request: RunAndAssertRequest
  ): Promise<RunAndAssertResponse> {
    return this.post<RunAndAssertResponse>(
      "/run-and-assert",
      request
    );
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
   * List files and directories under a target directory on the machine.
   */
  async listDirectory(request: ListDirectoryRequest): Promise<ListDirectoryResponse> {
    return this.post<ListDirectoryResponse>("/list-directory", request);
  }

  /**
   * Check whether a file or directory exists on the machine.
   */
  async fileExists(request: FileExistsRequest): Promise<FileExistsResponse> {
    return this.post<FileExistsResponse>("/file-exists", request);
  }

  /**
   * Assert that a tracked process exits (optionally with a specific exit code).
   */
  async assertProcessExited(request: AssertProcessExitedRequest): Promise<AssertionResponse> {
    return this.post<AssertionResponse>("/assert-process-exited", request);
  }

  /**
   * Assert that a path exists (optionally as a directory).
   */
  async assertPathExists(request: AssertPathExistsRequest): Promise<AssertionResponse> {
    return this.post<AssertionResponse>("/assert-path-exists", request);
  }

  /**
   * Assert that a text file contains the expected text.
   */
  async assertLogContains(request: AssertLogContainsRequest): Promise<AssertionResponse> {
    return this.post<AssertionResponse>("/assert-log-contains", request);
  }

  /**
   * Wait for a UI element to appear.
   */
  async waitForElement(request: WaitForElementRequest): Promise<WaitForElementResponse> {
    return this.post<WaitForElementResponse>("/wait-for-element", request);
  }

  /**
   * Focus a UI element.
   */
  async setFocus(request: SetFocusRequest): Promise<StatusResponse> {
    return this.post<StatusResponse>("/focus", request);
  }

  /**
   * Send keystrokes to the application window.
   */
  async sendKeys(request: SendKeysRequest): Promise<StatusResponse> {
    return this.post<StatusResponse>("/send-keys", request);
  }

  /**
   * Press a hotkey combination in the application window.
   */
  async pressHotkey(request: PressHotkeyRequest): Promise<StatusResponse> {
    return this.post<StatusResponse>("/press-hotkey", request);
  }
  /**
   * Set a checkbox or radio button to the requested checked state.
   */
  async setCheckbox(request: SetCheckboxRequest): Promise<StatusResponse> {
    return this.post<StatusResponse>("/set-checkbox", request);
  }

  /**
   * Select an option in a combo box or list by text or index.
   */
  async selectOption(request: SelectOptionRequest): Promise<StatusResponse> {
    return this.post<StatusResponse>("/select-option", request);
  }

  // ─── Helpers ─────────────────────────────────────────────────────────────

  private async get<T>(path: string): Promise<T> {
    const url = `${this.baseUrl}${path}`;
    const response = await fetch(url, {
      headers: this.buildHeaders(),
    });
    return this.handleResponse<T>(response);
  }

  private async post<T>(path: string, body: unknown): Promise<T> {
    const url = `${this.baseUrl}${path}`;
    const response = await fetch(url, {
      method: "POST",
      headers: {
        ...this.buildHeaders(),
        "Content-Type": "application/json",
      },
      body: JSON.stringify(body),
    });
    return this.handleResponse<T>(response);
  }

  private buildHeaders(): Record<string, string> {
    const headers: Record<string, string> = {};
    if (this.apiKey) {
      headers["X-API-Key"] = this.apiKey;
    }
    return headers;
  }

  private async handleResponse<T>(response: Response): Promise<T> {
    if (!response.ok) {
      let detail: string | undefined;
      let correlationId: string | undefined;
      let errorCode: string | undefined;
      let remediationHint: string | undefined;
      try {
        const err = (await response.clone().json()) as AgentError;
        detail = err.detail ?? err.message ?? err.error;
        correlationId = err.correlationId;
        errorCode = err.errorCode;
        remediationHint = err.remediationHint;
      } catch {
        detail = await response.text();
      }

      if (!correlationId) {
        correlationId = response.headers.get("X-Correlation-ID") ?? undefined;
      }

      throw new AgentClientError(
        response.status,
        detail ?? response.statusText,
        correlationId,
        errorCode,
        remediationHint);
    }
    return response.json() as Promise<T>;
  }
}

/**
 * Create an AgentClient using the URL from VS Code configuration.
 */
export function createAgentClient(): AgentClient {
  const config = vscode.workspace.getConfiguration("adagioAgent");
  const url = config.get<string>("vmAgentUrl") ?? "https://127.0.0.1:5443/api/v1";
  const requireHttps = config.get<boolean>("requireHttps") ?? true;
  const apiKey = config.get<string>("vmAgentApiKey");

  if (requireHttps && !url.toLowerCase().startsWith("https://")) {
    throw new Error(
      "adagioAgent.vmAgentUrl must use HTTPS when adagioAgent.requireHttps is true."
    );
  }

  return new AgentClient(url, apiKey);
}
