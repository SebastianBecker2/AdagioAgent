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

interface WaitForExitInput {
  pid: number;
  timeoutMilliseconds?: number;
}

interface ReadTextFileInput {
  path: string;
}

interface TailFileInput {
  path: string;
  lines?: number;
}

interface ElementStateInput {
  pid: number;
  elementId: string;
}

interface WaitForElementToolInput {
  pid: number;
  elementId: string;
  timeoutMilliseconds?: number;
  pollIntervalMilliseconds?: number;
}

interface SendKeysInput {
  pid: number;
  text: string;
}

interface PressHotkeyInput {
  pid: number;
  keys: string[];
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
    vscode.commands.registerCommand("adagioAgent.copyFile", cmdCopyFile),
    vscode.commands.registerCommand("adagioAgent.getProcessStatus", cmdGetProcessStatus),
    vscode.commands.registerCommand("adagioAgent.waitForExit", cmdWaitForExit),
    vscode.commands.registerCommand("adagioAgent.terminateProcess", cmdTerminateProcess),
    vscode.commands.registerCommand("adagioAgent.readTextFile", cmdReadTextFile),
    vscode.commands.registerCommand("adagioAgent.tailFile", cmdTailFile),
    vscode.commands.registerCommand("adagioAgent.getElementState", cmdGetElementState),
    vscode.commands.registerCommand("adagioAgent.waitForElement", cmdWaitForElementCommand),
    vscode.commands.registerCommand("adagioAgent.setFocus", cmdSetFocus),
    vscode.commands.registerCommand("adagioAgent.sendKeys", cmdSendKeys),
    vscode.commands.registerCommand("adagioAgent.pressHotkey", cmdPressHotkey)
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
      vscode.lm.registerTool("adagioAgent_copyFile", new CopyFileTool()),
      vscode.lm.registerTool("adagioAgent_getProcessStatus", new GetProcessStatusTool()),
      vscode.lm.registerTool("adagioAgent_waitForExit", new WaitForExitTool()),
      vscode.lm.registerTool("adagioAgent_terminateProcess", new TerminateProcessTool()),
      vscode.lm.registerTool("adagioAgent_readTextFile", new ReadTextFileTool()),
      vscode.lm.registerTool("adagioAgent_tailFile", new TailFileTool()),
      vscode.lm.registerTool("adagioAgent_getElementState", new GetElementStateTool()),
      vscode.lm.registerTool("adagioAgent_waitForElement", new WaitForElementUiTool()),
      vscode.lm.registerTool("adagioAgent_setFocus", new SetFocusTool()),
      vscode.lm.registerTool("adagioAgent_sendKeys", new SendKeysTool()),
      vscode.lm.registerTool("adagioAgent_pressHotkey", new PressHotkeyTool())
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

async function cmdGetProcessStatus(): Promise<void> {
  const pidStr = await vscode.window.showInputBox({ prompt: "Process ID" });
  if (!pidStr) {
    return;
  }

  const pid = Number(pidStr);
  if (!Number.isInteger(pid) || pid <= 0) {
    vscode.window.showErrorMessage("Invalid PID.");
    return;
  }

  const client = createAgentClient();
  const status = await client.getProcessStatus(pid);
  vscode.window.showInformationMessage(
    `Process ${status.pid}: ${status.status}` +
      (status.exitCode !== undefined ? ` (exit code ${status.exitCode})` : "")
  );
}

async function cmdWaitForExit(): Promise<void> {
  const pidStr = await vscode.window.showInputBox({ prompt: "Process ID" });
  if (!pidStr) {
    return;
  }

  const pid = Number(pidStr);
  if (!Number.isInteger(pid) || pid <= 0) {
    vscode.window.showErrorMessage("Invalid PID.");
    return;
  }

  const timeoutStr = await vscode.window.showInputBox({
    prompt: "Timeout milliseconds",
    value: "30000",
  });
  if (!timeoutStr) {
    return;
  }

  const timeoutMilliseconds = Number(timeoutStr);
  if (!Number.isInteger(timeoutMilliseconds) || timeoutMilliseconds <= 0) {
    vscode.window.showErrorMessage("Invalid timeout.");
    return;
  }

  const client = createAgentClient();
  const result = await client.waitForExit({ pid, timeoutMilliseconds });
  vscode.window.showInformationMessage(
    result.exited
      ? `Process ${pid} exited with status ${result.process.status}.`
      : `Process ${pid} is still running after timeout.`
  );
}

async function cmdTerminateProcess(): Promise<void> {
  const pidStr = await vscode.window.showInputBox({ prompt: "Process ID" });
  if (!pidStr) {
    return;
  }

  const pid = Number(pidStr);
  if (!Number.isInteger(pid) || pid <= 0) {
    vscode.window.showErrorMessage("Invalid PID.");
    return;
  }

  const client = createAgentClient();
  const result = await client.terminateProcess({ pid });
  if (result.status === "ok") {
    vscode.window.showInformationMessage(result.message ?? `Process ${pid} terminated.`);
  } else {
    vscode.window.showErrorMessage(result.message ?? `Failed to terminate process ${pid}.`);
  }
}

async function cmdReadTextFile(): Promise<void> {
  const path = await vscode.window.showInputBox({
    prompt: "Target machine path to text file",
    placeHolder: "C:\\Apps\\installer.log",
  });
  if (!path) {
    return;
  }

  const client = createAgentClient();
  const result = await client.readTextFile({ path });
  const doc = await vscode.workspace.openTextDocument({
    language: "log",
    content: result.content,
  });
  await vscode.window.showTextDocument(doc);
}

async function cmdTailFile(): Promise<void> {
  const path = await vscode.window.showInputBox({
    prompt: "Target machine path to text file",
    placeHolder: "C:\\Apps\\installer.log",
  });
  if (!path) {
    return;
  }

  const linesStr = await vscode.window.showInputBox({
    prompt: "Number of lines to read",
    value: "200",
  });
  if (!linesStr) {
    return;
  }

  const lines = Number(linesStr);
  if (!Number.isInteger(lines) || lines <= 0) {
    vscode.window.showErrorMessage("Invalid lines value.");
    return;
  }

  const client = createAgentClient();
  const result = await client.tailFile({ path, lines });
  const doc = await vscode.workspace.openTextDocument({
    language: "log",
    content: result.content,
  });
  await vscode.window.showTextDocument(doc);
}

async function cmdGetElementState(): Promise<void> {
  const pidStr = await vscode.window.showInputBox({ prompt: "Process ID" });
  if (!pidStr) {
    return;
  }

  const pid = Number(pidStr);
  if (!Number.isInteger(pid) || pid <= 0) {
    vscode.window.showErrorMessage("Invalid PID.");
    return;
  }

  const elementId = await vscode.window.showInputBox({ prompt: "Element ID" });
  if (!elementId) {
    return;
  }

  const client = createAgentClient();
  const result = await client.getElementState({ pid, elementId });
  vscode.window.showInformationMessage(
    `Element ${result.id}: ${result.type} '${result.name}'`
  );
}

async function cmdWaitForElementCommand(): Promise<void> {
  const pidStr = await vscode.window.showInputBox({ prompt: "Process ID" });
  if (!pidStr) {
    return;
  }

  const pid = Number(pidStr);
  if (!Number.isInteger(pid) || pid <= 0) {
    vscode.window.showErrorMessage("Invalid PID.");
    return;
  }

  const elementId = await vscode.window.showInputBox({ prompt: "Element ID" });
  if (!elementId) {
    return;
  }

  const timeoutStr = await vscode.window.showInputBox({
    prompt: "Timeout milliseconds",
    value: "30000",
  });
  if (!timeoutStr) {
    return;
  }

  const timeoutMilliseconds = Number(timeoutStr);
  if (!Number.isInteger(timeoutMilliseconds) || timeoutMilliseconds <= 0) {
    vscode.window.showErrorMessage("Invalid timeout.");
    return;
  }

  const client = createAgentClient();
  const result = await client.waitForElement({
    pid,
    elementId,
    timeoutMilliseconds,
  });

  vscode.window.showInformationMessage(
    result.found
      ? `Element '${elementId}' became available.`
      : `Element '${elementId}' was not found before timeout.`
  );
}

async function cmdSetFocus(): Promise<void> {
  const pidStr = await vscode.window.showInputBox({ prompt: "Process ID" });
  if (!pidStr) {
    return;
  }

  const pid = Number(pidStr);
  if (!Number.isInteger(pid) || pid <= 0) {
    vscode.window.showErrorMessage("Invalid PID.");
    return;
  }

  const elementId = await vscode.window.showInputBox({ prompt: "Element ID" });
  if (!elementId) {
    return;
  }

  const client = createAgentClient();
  const result = await client.setFocus({ pid, elementId });
  if (result.status === "ok") {
    vscode.window.showInformationMessage(`Focused element '${elementId}'.`);
  } else {
    vscode.window.showErrorMessage(result.message ?? `Failed to focus '${elementId}'.`);
  }
}

async function cmdSendKeys(): Promise<void> {
  const pidStr = await vscode.window.showInputBox({ prompt: "Process ID" });
  if (!pidStr) {
    return;
  }

  const pid = Number(pidStr);
  if (!Number.isInteger(pid) || pid <= 0) {
    vscode.window.showErrorMessage("Invalid PID.");
    return;
  }

  const text = await vscode.window.showInputBox({ prompt: "Keys/text to send" });
  if (text === undefined || text.length === 0) {
    return;
  }

  const client = createAgentClient();
  const result = await client.sendKeys({ pid, text });
  if (result.status === "ok") {
    vscode.window.showInformationMessage(`Sent keys to process ${pid}.`);
  } else {
    vscode.window.showErrorMessage(result.message ?? `Failed to send keys to process ${pid}.`);
  }
}

async function cmdPressHotkey(): Promise<void> {
  const pidStr = await vscode.window.showInputBox({ prompt: "Process ID" });
  if (!pidStr) {
    return;
  }

  const pid = Number(pidStr);
  if (!Number.isInteger(pid) || pid <= 0) {
    vscode.window.showErrorMessage("Invalid PID.");
    return;
  }

  const keysStr = await vscode.window.showInputBox({
    prompt: "Hotkey combination (comma-separated)",
    placeHolder: "alt,n",
  });
  if (!keysStr) {
    return;
  }

  const keys = keysStr.split(",").map((key) => key.trim()).filter(Boolean);
  if (keys.length === 0) {
    vscode.window.showErrorMessage("Invalid hotkey.");
    return;
  }

  const client = createAgentClient();
  const result = await client.pressHotkey({ pid, keys });
  if (result.status === "ok") {
    vscode.window.showInformationMessage(`Pressed hotkey ${keys.join("+")} on process ${pid}.`);
  } else {
    vscode.window.showErrorMessage(result.message ?? `Failed to press hotkey on process ${pid}.`);
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
    const imageBytes = new Uint8Array(Buffer.from(screenshot.imageBase64, "base64"));
    return new vscode.LanguageModelToolResult([
      new vscode.LanguageModelTextPart(
        `Screenshot captured for process ${options.input.pid}.`
      ),
      vscode.LanguageModelDataPart.image(imageBytes, "image/png"),
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

class GetProcessStatusTool implements vscode.LanguageModelTool<PidInput> {
  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<PidInput>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const client = createAgentClient();
    const status = await client.getProcessStatus(options.input.pid);
    const parts = [
      `PID: ${status.pid}`,
      `Status: ${status.status}`,
      `Started: ${status.startedAt}`,
    ];
    if (status.exitedAt) {
      parts.push(`Exited: ${status.exitedAt}`);
    }
    if (status.exitCode !== undefined) {
      parts.push(`Exit code: ${status.exitCode}`);
    }
    return new vscode.LanguageModelToolResult([
      new vscode.LanguageModelTextPart(parts.join("\n")),
    ]);
  }
}

class WaitForExitTool implements vscode.LanguageModelTool<WaitForExitInput> {
  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<WaitForExitInput>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const client = createAgentClient();
    const timeoutMilliseconds = options.input.timeoutMilliseconds ?? 30000;
    const result = await client.waitForExit({
      pid: options.input.pid,
      timeoutMilliseconds,
    });

    return new vscode.LanguageModelToolResult([
      new vscode.LanguageModelTextPart(
        result.exited
          ? `Process ${result.process.pid} exited with status ${result.process.status}.`
          : `Process ${result.process.pid} is still running after ${timeoutMilliseconds}ms.`
      ),
    ]);
  }
}

class TerminateProcessTool implements vscode.LanguageModelTool<PidInput> {
  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<PidInput>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const client = createAgentClient();
    const result = await client.terminateProcess({ pid: options.input.pid });
    return new vscode.LanguageModelToolResult([
      new vscode.LanguageModelTextPart(result.message ?? `Process ${options.input.pid} terminated.`),
    ]);
  }
}

class ReadTextFileTool implements vscode.LanguageModelTool<ReadTextFileInput> {
  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<ReadTextFileInput>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const client = createAgentClient();
    const result = await client.readTextFile({ path: options.input.path });
    return new vscode.LanguageModelToolResult([
      new vscode.LanguageModelTextPart(
        `File: ${result.path}\n\n${result.content}`
      ),
    ]);
  }
}

class TailFileTool implements vscode.LanguageModelTool<TailFileInput> {
  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<TailFileInput>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const client = createAgentClient();
    const lines = options.input.lines ?? 200;
    const result = await client.tailFile({
      path: options.input.path,
      lines,
    });
    return new vscode.LanguageModelToolResult([
      new vscode.LanguageModelTextPart(
        `Tail (${result.lines}) ${result.path}\n\n${result.content}`
      ),
    ]);
  }
}

class GetElementStateTool implements vscode.LanguageModelTool<ElementStateInput> {
  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<ElementStateInput>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const client = createAgentClient();
    const result = await client.getElementState(options.input);
    return new vscode.LanguageModelToolResult([
      new vscode.LanguageModelTextPart(
        `Element ${result.id}\nType: ${result.type}\nName: ${result.name}\nAvailable: ${result.available}`
      ),
    ]);
  }
}

class WaitForElementUiTool implements vscode.LanguageModelTool<WaitForElementToolInput> {
  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<WaitForElementToolInput>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const client = createAgentClient();
    const result = await client.waitForElement({
      pid: options.input.pid,
      elementId: options.input.elementId,
      timeoutMilliseconds: options.input.timeoutMilliseconds ?? 30000,
      pollIntervalMilliseconds: options.input.pollIntervalMilliseconds,
    });

    return new vscode.LanguageModelToolResult([
      new vscode.LanguageModelTextPart(
        result.found
          ? `Element '${options.input.elementId}' is available.`
          : `Element '${options.input.elementId}' was not found before timeout.`
      ),
    ]);
  }
}

class SetFocusTool implements vscode.LanguageModelTool<ElementStateInput> {
  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<ElementStateInput>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const client = createAgentClient();
    const result = await client.setFocus(options.input);
    return new vscode.LanguageModelToolResult([
      new vscode.LanguageModelTextPart(
        result.message ?? `Focused element '${options.input.elementId}'.`
      ),
    ]);
  }
}

class SendKeysTool implements vscode.LanguageModelTool<SendKeysInput> {
  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<SendKeysInput>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const client = createAgentClient();
    const result = await client.sendKeys(options.input);
    return new vscode.LanguageModelToolResult([
      new vscode.LanguageModelTextPart(
        result.message ?? `Sent keys to process ${options.input.pid}.`
      ),
    ]);
  }
}

class PressHotkeyTool implements vscode.LanguageModelTool<PressHotkeyInput> {
  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<PressHotkeyInput>,
    _token: vscode.CancellationToken
  ): Promise<vscode.LanguageModelToolResult> {
    const client = createAgentClient();
    const result = await client.pressHotkey(options.input);
    return new vscode.LanguageModelToolResult([
      new vscode.LanguageModelTextPart(
        result.message ?? `Pressed hotkey ${options.input.keys.join("+")} on process ${options.input.pid}.`
      ),
    ]);
  }
}

// Re-export AgentClient for testing / external use
export { AgentClient };
