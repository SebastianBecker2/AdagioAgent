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
}

export interface AgentError {
  error: string;
  detail?: string;
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

export interface RunInstallerAndCollectArtifactsResponse {
  pid: number;
  startedAt: string;
  artifacts: CollectInstallArtifactsResponse;
}

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

export interface WaitForElementResponse {
  found: boolean;
  element?: ElementStateResponse;
}
