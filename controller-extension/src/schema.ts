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

// ─── Responses ───────────────────────────────────────────────────────────────

export interface RunResponse {
  pid: number;
  status: "running" | "exited" | "error";
  startedAt: string;
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

export interface ScreenshotResponse {
  imageBase64: string;
}

export interface ClickResponse {
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
