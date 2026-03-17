import * as vscode from "vscode";

function toErrorMessage(error: unknown): string {
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
  handler: (...args: TArgs) => Promise<void> | void
): (...args: TArgs) => Promise<void> {
  return async (...args: TArgs): Promise<void> => {
    try {
      await handler(...args);
    } catch (error) {
      vscode.window.showErrorMessage(`Adagio Agent command failed: ${toErrorMessage(error)}`);
    }
  };
}
