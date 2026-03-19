import * as vscode from "vscode";

function toErrorMessage(error: unknown): string {
  if (typeof error === "object" && error !== null) {
    const candidate = error as { message?: unknown };
    if (typeof candidate.message === "string" && candidate.message.length > 0) {
      return candidate.message;
    }
  }

  if (error instanceof Error && error.message) {
    return error.message;
  }

  if (typeof error === "string" && error.length > 0) {
    return error;
  }

  return "Unexpected error while running command.";
}

/**
 * Wrap a command handler to ensure consistent user-facing error reporting.
 */
export function wrapCommand<TArgs extends unknown[]>(
  handler: (...args: TArgs) => Promise<void> | void,
  reportError?: (message: string) => void
): (...args: TArgs) => Promise<void> {
  return async (...args: TArgs): Promise<void> => {
    try {
      await handler(...args);
    } catch (error) {
      const reporter = reportError ?? vscode.window.showErrorMessage;
      reporter(`Adagio Agent command failed: ${toErrorMessage(error)}`);
    }
  };
}
