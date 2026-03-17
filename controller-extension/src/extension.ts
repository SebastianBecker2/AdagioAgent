import * as vscode from "vscode";
import * as os from "os";
import { AgentClient, createAgentClient } from "./agentClient";
import { RunRequest, UiElement } from "./schema";

// ─── Tool handler types ──────────────────────────────────────────────────────

interface RunExecutableInput {
  command: string;
  arguments?: string;
  workingDirectory?: string;
}

interface PidInput {
  pid: number;
}

interface ClickInput {
  pid: number;
  elementId: string;
}

interface TypeInput {
  pid: number;
  elementId: string;
  text: string;
}

// ─── Activation ──────────────────────────────────────────────────────────────

export function activate(context: vscode.ExtensionContext): void {
  // ── VS Code commands (palette / keybindings) ─────────────────────────────

  context.subscriptions.push(
    vscode.commands.registerCommand(
      "adagioAgent.runExecutable",
      cmdRunExecutable
    ),
    vscode.commands.registerCommand("adagioAgent.getUiTree", cmdGetUiTree),
    vscode.commands.registerCommand(
      "adagioAgent.clickElement",
      cmdClickElement
    ),
    vscode.commands.registerCommand(
      "adagioAgent.getScreenshot",
      cmdGetScreenshot
    ),
    vscode.commands.registerCommand("adagioAgent.typeText", cmdTypeText),
    vscode.commands.registerCommand("adagioAgent.copyFile", cmdCopyFile)
  );

  // ── Copilot language-model tools ─────────────────────────────────────────

  if (typeof vscode.lm !== "undefined" && "registerTool" in vscode.lm) {
    context.subscriptions.push(
      vscode.lm.registerTool(
        "adagioAgent_runExecutable",
        new RunExecutableTool()
      ),
      vscode.lm.registerTool("adagioAgent_getUiTree", new GetUiTreeTool()),
      vscode.lm.registerTool(
        "adagioAgent_getScreenshot",
        new GetScreenshotTool()
      ),
      vscode.lm.registerTool(
        "adagioAgent_clickElement",
        new ClickElementTool()
      ),
      vscode.lm.registerTool("adagioAgent_typeText", new TypeTextTool()),
      vscode.lm.registerTool("adagioAgent_copyFile", new CopyFileTool())
    );
  }
}

export function deactivate(): void {
  // Nothing to clean up
}

// ─── Palette command implementations ─────────────────────────────────────────

async function cmdRunExecutable(): Promise<void> {
  const command = await vscode.window.showInputBox({
    prompt: "Full path to executable on the VM (e.g. C:\\Apps\\MyApp.exe)",
    placeHolder: "C:\\Apps\\MyApp.exe",
  });
  if (!command) {
    return;
  }

  const client = createAgentClient();
  await vscode.window.withProgress(
    {
      location: vscode.ProgressLocation.Notification,
      title: "Adagio Agent: Starting executable…",
      cancellable: false,
    },
    async () => {
      const result = await client.runExecutable({ command });
      vscode.window.showInformationMessage(
        `Executable started – PID ${result.pid} (${result.status})`
      );
    }
  );
}

async function cmdGetUiTree(): Promise<void> {
  const pidStr = await vscode.window.showInputBox({
    prompt: "Process ID of the executable",
    placeHolder: "1234",
  });
  if (!pidStr) {
    return;
  }
  const pid = Number(pidStr);
  if (!Number.isInteger(pid) || pid <= 0) {
    vscode.window.showErrorMessage("Invalid PID.");
    return;
  }

  const client = createAgentClient();
  const tree = await client.getUiTree(pid);

  const doc = await vscode.workspace.openTextDocument({
    language: "json",
    content: JSON.stringify(tree, null, 2),
  });
  await vscode.window.showTextDocument(doc);
}

async function cmdClickElement(): Promise<void> {
  const pidStr = await vscode.window.showInputBox({
    prompt: "Process ID",
  });
  if (!pidStr) {
    return;
  }
  const pid = Number(pidStr);
  if (!Number.isInteger(pid) || pid <= 0) {
    vscode.window.showErrorMessage("Invalid PID.");
    return;
  }

  const elementId = await vscode.window.showInputBox({
    prompt: "Element ID (from UI tree)",
  });
  if (!elementId) {
    return;
  }

  const client = createAgentClient();
  const result = await client.clickElement(pid, elementId);
  if (result.status === "ok") {
    vscode.window.showInformationMessage(`Clicked element '${elementId}'.`);
  } else {
    vscode.window.showErrorMessage(
      `Click failed: ${result.message ?? "unknown error"}`
    );
  }
}

async function cmdGetScreenshot(): Promise<void> {
  const pidStr = await vscode.window.showInputBox({
    prompt: "Process ID",
  });
  if (!pidStr) {
    return;
  }
  const pid = Number(pidStr);
  if (!Number.isInteger(pid) || pid <= 0) {
    vscode.window.showErrorMessage("Invalid PID.");
    return;
  }

  const client = createAgentClient();
  const screenshot = await client.getScreenshot(pid);

  // Write the image to a temp file and open it
  const tmpUri = vscode.Uri.joinPath(
    vscode.Uri.file(os.tmpdir()),
    `adagio-screenshot-${pid}-${Date.now()}.png`
  );
  const imageBytes = new Uint8Array(Buffer.from(screenshot.imageBase64, "base64"));
  await vscode.workspace.fs.writeFile(tmpUri, imageBytes);
  await vscode.commands.executeCommand("vscode.open", tmpUri);
}

async function cmdTypeText(): Promise<void> {
  const pidStr = await vscode.window.showInputBox({ prompt: "Process ID" });
  if (!pidStr) {
    return;
  }
  const pid = Number(pidStr);
  if (!Number.isInteger(pid) || pid <= 0) {
    vscode.window.showErrorMessage("Invalid PID.");
    return;
  }

  const elementId = await vscode.window.showInputBox({
    prompt: "Element ID",
  });
  if (!elementId) {
    return;
  }

  const text = await vscode.window.showInputBox({ prompt: "Text to type" });
  if (text === undefined) {
    return;
  }

  const client = createAgentClient();
  const result = await client.typeText(pid, elementId, text);
  if (result.status === "ok") {
    vscode.window.showInformationMessage(
      `Typed text into element '${elementId}'.`
    );
  } else {
    vscode.window.showErrorMessage(
      `Type failed: ${result.message ?? "unknown error"}`
    );
  }
}

async function cmdCopyFile(): Promise<void> {
  const filePath = await vscode.window.showInputBox({
    prompt: "Path to file to copy (on local machine)",
    placeHolder: "C:\\path\\to\\file.txt",
  });
  if (!filePath) {
    return;
  }

  const destinationPath = await vscode.window.showInputBox({
    prompt: "Destination path on target system",
    placeHolder: "C:\\Apps\\file.txt",
  });
  if (!destinationPath) {
    return;
  }

  try {
    const fileUri = vscode.Uri.file(filePath);
    const fileBytes = await vscode.workspace.fs.readFile(fileUri);
    const base64Content = Buffer.from(fileBytes).toString("base64");

    const client = createAgentClient();
    const result = await client.copyFile({
      destinationPath,
      fileContentBase64: base64Content,
      overwriteIfExists: false,
    });

    vscode.window.showInformationMessage(
      `File copied successfully (${result.bytesWritten} bytes)`
    );
  } catch (err) {
    vscode.window.showErrorMessage(
      `Failed to copy file: ${err}`
    );
  }
}

// ─── Copilot tool implementations ────────────────────────────────────────────

function uiTreeSummary(elements: UiElement[], depth = 0): string {
  return elements
    .map((el) => {
      const indent = "  ".repeat(depth);
      const bounds = el.bounds
        ? ` [${el.bounds.x},${el.bounds.y} ${el.bounds.width}×${el.bounds.height}]`
        : "";
      const line = `${indent}${el.type} "${el.name}" id=${el.id}${bounds}`;
      const children =
        el.children && el.children.length > 0
          ? "\n" + uiTreeSummary(el.children, depth + 1)
          : "";
      return line + children;
    })
    .join("\n");
}

class RunExecutableTool implements vscode.LanguageModelTool<RunExecutableInput> {
  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<RunExecutableInput>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const { command, arguments: args, workingDirectory } = options.input;
    const request: RunRequest = { command, arguments: args, workingDirectory };
    const client = createAgentClient();
    const result = await client.runExecutable(request);
    return new vscode.LanguageModelToolResult([
      new vscode.LanguageModelTextPart(
        `Executable started.\n- PID: ${result.pid}\n- Status: ${result.status}\n- Started at: ${result.startedAt}`
      ),
    ]);
  }
}

class GetUiTreeTool implements vscode.LanguageModelTool<PidInput> {
  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<PidInput>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const client = createAgentClient();
    const tree = await client.getUiTree(options.input.pid);
    const summary = uiTreeSummary(tree.elements);
    return new vscode.LanguageModelToolResult([
      new vscode.LanguageModelTextPart(
        `Window: "${tree.windowTitle}"\n\nUI elements:\n${summary}`
      ),
    ]);
  }
}

class GetScreenshotTool implements vscode.LanguageModelTool<PidInput> {
  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<PidInput>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const client = createAgentClient();
    const screenshot = await client.getScreenshot(options.input.pid);
    return new vscode.LanguageModelToolResult([
      new vscode.LanguageModelTextPart(
        `Screenshot captured (base64 PNG, ${screenshot.imageBase64.length} chars).`
      ),
    ]);
  }
}

class ClickElementTool implements vscode.LanguageModelTool<ClickInput> {
  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<ClickInput>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const { pid, elementId } = options.input;
    const client = createAgentClient();
    const result = await client.clickElement(pid, elementId);
    const text =
      result.status === "ok"
        ? `Successfully clicked element '${elementId}'.`
        : `Failed to click element '${elementId}': ${result.message ?? "unknown error"}`;
    return new vscode.LanguageModelToolResult([
      new vscode.LanguageModelTextPart(text),
    ]);
  }
}

class TypeTextTool implements vscode.LanguageModelTool<TypeInput> {
  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<TypeInput>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const { pid, elementId, text } = options.input;
    const client = createAgentClient();
    const result = await client.typeText(pid, elementId, text);
    const msg =
      result.status === "ok"
        ? `Successfully typed text into element '${elementId}'.`
        : `Failed to type text into element '${elementId}': ${result.message ?? "unknown error"}`;
    return new vscode.LanguageModelToolResult([
      new vscode.LanguageModelTextPart(msg),
    ]);
  }
}

interface CopyFileInput {
  localFilePath: string;
  destinationPath: string;
  overwriteIfExists?: boolean;
}

class CopyFileTool implements vscode.LanguageModelTool<CopyFileInput> {
  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<CopyFileInput>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const { localFilePath, destinationPath, overwriteIfExists } = options.input;
    const fileUri = vscode.Uri.file(localFilePath);
    const fileBytes = await vscode.workspace.fs.readFile(fileUri);
    const base64Content = Buffer.from(fileBytes).toString("base64");

    const client = createAgentClient();
    const result = await client.copyFile({
      destinationPath,
      fileContentBase64: base64Content,
      overwriteIfExists: overwriteIfExists ?? true,
    });

    return new vscode.LanguageModelToolResult([
      new vscode.LanguageModelTextPart(
        `File copied to ${result.destinationPath} (${result.bytesWritten} bytes)`
      ),
    ]);
  }
}

// Re-export AgentClient for testing / external use
export { AgentClient };
