/**
 * Type definitions for the Adagio Agent REST API.
 * These match the contract described in the architecture spec.
 */

// ─── Requests ────────────────────────────────────────────────────────────────

export interface RunRequest {
  command: string;
  arguments?: string;
  workingDirectory?: string;
}

export interface ClickRequest {
  pid: number;
  elementId: string;
}

export interface TypeRequest {
  pid: number;
  elementId: string;
  text: string;
}

export interface CopyFileRequest {
  destinationPath: string;
  fileContentBase64: string;
  overwriteIfExists?: boolean;
}

export interface WaitForExitRequest {
  pid: number;
  timeoutMilliseconds?: number;
}

export interface CollectInstallArtifactsRequest {
  pid: number;
  timeoutMilliseconds?: number;
  logPath?: string;
  tailLines?: number;
  includeMsiEvents?: boolean;
  eventEntryCount?: number;
}

export type CollectProcessArtifactsRequest = CollectInstallArtifactsRequest;

export interface RunInstallerAndCollectArtifactsRequest {
  command: string;
  arguments?: string;
  workingDirectory?: string;
  timeoutMilliseconds?: number;
  logPath?: string;
  tailLines?: number;
  includeMsiEvents?: boolean;
  eventEntryCount?: number;
}

export type RunAndCollectArtifactsRequest = RunInstallerAndCollectArtifactsRequest;

export interface RunInstallerAndAssertRequest {
  command: string;
  arguments?: string;
  workingDirectory?: string;
  timeoutMilliseconds?: number;
  logPath?: string;
  tailLines?: number;
  includeMsiEvents?: boolean;
  eventEntryCount?: number;
  expectedExitCode?: number;
  expectedPath?: string;
  expectedPathMustBeDirectory?: boolean;
  logMustContainText?: string;
  logContainsIgnoreCase?: boolean;
}

export type RunAndAssertRequest = RunInstallerAndAssertRequest;

export interface TerminateProcessRequest {
  pid: number;
}

export interface ReadTextFileRequest {
  path: string;
}

export interface TailFileRequest {
  path: string;
  lines?: number;
}

export interface ListDirectoryRequest {
  path: string;
}

export interface FileExistsRequest {
  path: string;
}

export interface AssertProcessExitedRequest {
  pid: number;
  timeoutMilliseconds?: number;
  expectedExitCode?: number;
}

export interface AssertPathExistsRequest {
  path: string;
  mustBeDirectory?: boolean;
}

export interface AssertLogContainsRequest {
  path: string;
  containsText: string;
  ignoreCase?: boolean;
}

export interface ElementStateRequest {
  pid: number;
  elementId: string;
}

export interface WaitForElementRequest {
  pid: number;
  elementId: string;
  timeoutMilliseconds?: number;
  pollIntervalMilliseconds?: number;
}

export interface SetFocusRequest {
  pid: number;
  elementId: string;
}

export interface SendKeysRequest {
  pid: number;
  text: string;
}

export interface PressHotkeyRequest {
  pid: number;
  keys: string[];
}

// ─── Responses ───────────────────────────────────────────────────────────────
export interface SetCheckboxRequest {
  pid: number;
  elementId: string;
  isChecked: boolean;
}

export interface SelectOptionRequest {
  pid: number;
  elementId: string;
  optionText?: string;
  optionIndex?: number;
}

// ─── Responses ───────────────────────────────────────────────────────────────

export interface RunResponse {
  pid: number;
  status: "running" | "exited" | "error";
  startedAt: string;
}

export interface ProcessStatusResponse {
  pid: number;
  status: "running" | "exited" | "error";
  startedAt: string;
  exitedAt?: string;
  exitCode?: number;
}

export interface Bounds {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface UiElement {
  id: string;
  type: string;
  name: string;
  automationId: string;
  bounds?: Bounds;
  children?: UiElement[];
}

export interface UiTreeResponse {
  windowTitle: string;
  elements: UiElement[];
}

export interface ElementStateResponse {
  id: string;
  type: string;
  name: string;
  automationId: string;
  bounds?: Bounds;
  available: boolean;
}

export interface ScreenshotResponse {
  imageBase64: string;
}

export interface ClickResponse {
  status: "ok" | "error";
  message?: string;
}

export interface StatusResponse {
  status: "ok" | "error";
  message?: string;
}

export interface TypeResponse {
  status: "ok" | "error";
  message?: string;
}

export interface HealthResponse {
  status: "healthy";
  version: string;
  apiVersion: number;
  minSupportedClientVersion?: string;
  maxSupportedClientVersion?: string;
}

export interface ReadinessResponse {
  status: "ready" | "degraded";
  version: string;
  apiVersion: number;
  platform: "windows" | "linux" | "unsupported";
  uiAutomationAvailable: boolean;
  issues: string[];
}

export interface DiagnosticsStatusResponse {
  status: "ready" | "degraded";
  version: string;
  apiVersion: number;
  platform: "windows" | "linux" | "unsupported";
  uiAutomationAvailable: boolean;
  issues: string[];
  runningProcessCount: number;
  trackedProcessCount: number;
  timestampUtc: string;
}

export interface AgentError {
  error: string;
  detail?: string;
  correlationId?: string;
  /** Machine-readable error code (e.g. ELEMENT_NOT_FOUND, PROCESS_NOT_FOUND). */
  errorCode?: string;
  /** Human-readable remediation hint targeted at the caller. */
  remediationHint?: string;
}

export interface CopyFileResponse {
  destinationPath: string;
  bytesWritten: number;
}

export interface WaitForExitResponse {
  exited: boolean;
  process: ProcessStatusResponse;
}

export interface InstallEventLogEntry {
  timeCreated: string;
  eventId: number;
  level: string;
  source: string;
  message: string;
}

export interface CollectInstallArtifactsResponse {
  exited: boolean;
  process: ProcessStatusResponse;
  logTail?: TailFileResponse;
  msiEvents: InstallEventLogEntry[];
  warnings: string[];
}

export type CollectProcessArtifactsResponse = CollectInstallArtifactsResponse;

export interface RunInstallerAndCollectArtifactsResponse {
  pid: number;
  startedAt: string;
  artifacts: CollectInstallArtifactsResponse;
}

export type RunAndCollectArtifactsResponse = RunInstallerAndCollectArtifactsResponse;

export interface RunInstallerAndAssertResponse {
  pid: number;
  startedAt: string;
  artifacts: CollectInstallArtifactsResponse;
  assertions: AssertionResponse[];
  passed: boolean;
}

export type RunAndAssertResponse = RunInstallerAndAssertResponse;

export interface ReadTextFileResponse {
  path: string;
  content: string;
}

export interface TailFileResponse {
  path: string;
  lines: number;
  content: string;
}

export interface DirectoryEntry {
  name: string;
  path: string;
  isDirectory: boolean;
}

export interface ListDirectoryResponse {
  path: string;
  entries: DirectoryEntry[];
}

export interface FileExistsResponse {
  path: string;
  exists: boolean;
  isDirectory: boolean;
}

export interface AssertionResponse {
  passed: boolean;
  message: string;
}

export interface WaitForElementResponse {
  found: boolean;
  element?: ElementStateResponse;
}
