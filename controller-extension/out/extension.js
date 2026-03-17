"use strict";
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", { value: true });
exports.AgentClient = void 0;
exports.activate = activate;
exports.deactivate = deactivate;
const vscode = __importStar(require("vscode"));
const os = __importStar(require("os"));
const agentClient_1 = require("./agentClient");
Object.defineProperty(exports, "AgentClient", { enumerable: true, get: function () { return agentClient_1.AgentClient; } });
// ─── Activation ──────────────────────────────────────────────────────────────
function activate(context) {
    // ── VS Code commands (palette / keybindings) ─────────────────────────────
    context.subscriptions.push(vscode.commands.registerCommand("adagioAgent.runExecutable", cmdRunExecutable), vscode.commands.registerCommand("adagioAgent.getUiTree", cmdGetUiTree), vscode.commands.registerCommand("adagioAgent.clickElement", cmdClickElement), vscode.commands.registerCommand("adagioAgent.getScreenshot", cmdGetScreenshot), vscode.commands.registerCommand("adagioAgent.typeText", cmdTypeText), vscode.commands.registerCommand("adagioAgent.copyFile", cmdCopyFile), vscode.commands.registerCommand("adagioAgent.getProcessStatus", cmdGetProcessStatus), vscode.commands.registerCommand("adagioAgent.waitForExit", cmdWaitForExit), vscode.commands.registerCommand("adagioAgent.terminateProcess", cmdTerminateProcess), vscode.commands.registerCommand("adagioAgent.readTextFile", cmdReadTextFile), vscode.commands.registerCommand("adagioAgent.tailFile", cmdTailFile), vscode.commands.registerCommand("adagioAgent.getElementState", cmdGetElementState), vscode.commands.registerCommand("adagioAgent.waitForElement", cmdWaitForElementCommand));
    // ── Copilot language-model tools ─────────────────────────────────────────
    if (typeof vscode.lm !== "undefined" && "registerTool" in vscode.lm) {
        context.subscriptions.push(vscode.lm.registerTool("adagioAgent_runExecutable", new RunExecutableTool()), vscode.lm.registerTool("adagioAgent_getUiTree", new GetUiTreeTool()), vscode.lm.registerTool("adagioAgent_getScreenshot", new GetScreenshotTool()), vscode.lm.registerTool("adagioAgent_clickElement", new ClickElementTool()), vscode.lm.registerTool("adagioAgent_typeText", new TypeTextTool()), vscode.lm.registerTool("adagioAgent_copyFile", new CopyFileTool()), vscode.lm.registerTool("adagioAgent_getProcessStatus", new GetProcessStatusTool()), vscode.lm.registerTool("adagioAgent_waitForExit", new WaitForExitTool()), vscode.lm.registerTool("adagioAgent_terminateProcess", new TerminateProcessTool()), vscode.lm.registerTool("adagioAgent_readTextFile", new ReadTextFileTool()), vscode.lm.registerTool("adagioAgent_tailFile", new TailFileTool()), vscode.lm.registerTool("adagioAgent_getElementState", new GetElementStateTool()), vscode.lm.registerTool("adagioAgent_waitForElement", new WaitForElementUiTool()));
    }
}
function deactivate() {
    // Nothing to clean up
}
// ─── Palette command implementations ─────────────────────────────────────────
async function cmdRunExecutable() {
    const command = await vscode.window.showInputBox({
        prompt: "Full path to executable on the VM (e.g. C:\\Apps\\MyApp.exe)",
        placeHolder: "C:\\Apps\\MyApp.exe",
    });
    if (!command) {
        return;
    }
    const client = (0, agentClient_1.createAgentClient)();
    await vscode.window.withProgress({
        location: vscode.ProgressLocation.Notification,
        title: "Adagio Agent: Starting executable…",
        cancellable: false,
    }, async () => {
        const result = await client.runExecutable({ command });
        vscode.window.showInformationMessage(`Executable started – PID ${result.pid} (${result.status})`);
    });
}
async function cmdGetUiTree() {
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
    const client = (0, agentClient_1.createAgentClient)();
    const tree = await client.getUiTree(pid);
    const doc = await vscode.workspace.openTextDocument({
        language: "json",
        content: JSON.stringify(tree, null, 2),
    });
    await vscode.window.showTextDocument(doc);
}
async function cmdClickElement() {
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
    const client = (0, agentClient_1.createAgentClient)();
    const result = await client.clickElement(pid, elementId);
    if (result.status === "ok") {
        vscode.window.showInformationMessage(`Clicked element '${elementId}'.`);
    }
    else {
        vscode.window.showErrorMessage(`Click failed: ${result.message ?? "unknown error"}`);
    }
}
async function cmdGetScreenshot() {
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
    const client = (0, agentClient_1.createAgentClient)();
    const screenshot = await client.getScreenshot(pid);
    // Write the image to a temp file and open it
    const tmpUri = vscode.Uri.joinPath(vscode.Uri.file(os.tmpdir()), `adagio-screenshot-${pid}-${Date.now()}.png`);
    const imageBytes = new Uint8Array(Buffer.from(screenshot.imageBase64, "base64"));
    await vscode.workspace.fs.writeFile(tmpUri, imageBytes);
    await vscode.commands.executeCommand("vscode.open", tmpUri);
}
async function cmdTypeText() {
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
    const client = (0, agentClient_1.createAgentClient)();
    const result = await client.typeText(pid, elementId, text);
    if (result.status === "ok") {
        vscode.window.showInformationMessage(`Typed text into element '${elementId}'.`);
    }
    else {
        vscode.window.showErrorMessage(`Type failed: ${result.message ?? "unknown error"}`);
    }
}
async function cmdCopyFile() {
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
        const client = (0, agentClient_1.createAgentClient)();
        const result = await client.copyFile({
            destinationPath,
            fileContentBase64: base64Content,
            overwriteIfExists: false,
        });
        vscode.window.showInformationMessage(`File copied successfully (${result.bytesWritten} bytes)`);
    }
    catch (err) {
        vscode.window.showErrorMessage(`Failed to copy file: ${err}`);
    }
}
async function cmdGetProcessStatus() {
    const pidStr = await vscode.window.showInputBox({ prompt: "Process ID" });
    if (!pidStr) {
        return;
    }
    const pid = Number(pidStr);
    if (!Number.isInteger(pid) || pid <= 0) {
        vscode.window.showErrorMessage("Invalid PID.");
        return;
    }
    const client = (0, agentClient_1.createAgentClient)();
    const status = await client.getProcessStatus(pid);
    vscode.window.showInformationMessage(`Process ${status.pid}: ${status.status}` +
        (status.exitCode !== undefined ? ` (exit code ${status.exitCode})` : ""));
}
async function cmdWaitForExit() {
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
    const client = (0, agentClient_1.createAgentClient)();
    const result = await client.waitForExit({ pid, timeoutMilliseconds });
    vscode.window.showInformationMessage(result.exited
        ? `Process ${pid} exited with status ${result.process.status}.`
        : `Process ${pid} is still running after timeout.`);
}
async function cmdTerminateProcess() {
    const pidStr = await vscode.window.showInputBox({ prompt: "Process ID" });
    if (!pidStr) {
        return;
    }
    const pid = Number(pidStr);
    if (!Number.isInteger(pid) || pid <= 0) {
        vscode.window.showErrorMessage("Invalid PID.");
        return;
    }
    const client = (0, agentClient_1.createAgentClient)();
    const result = await client.terminateProcess({ pid });
    if (result.status === "ok") {
        vscode.window.showInformationMessage(result.message ?? `Process ${pid} terminated.`);
    }
    else {
        vscode.window.showErrorMessage(result.message ?? `Failed to terminate process ${pid}.`);
    }
}
async function cmdReadTextFile() {
    const path = await vscode.window.showInputBox({
        prompt: "Target machine path to text file",
        placeHolder: "C:\\Apps\\installer.log",
    });
    if (!path) {
        return;
    }
    const client = (0, agentClient_1.createAgentClient)();
    const result = await client.readTextFile({ path });
    const doc = await vscode.workspace.openTextDocument({
        language: "log",
        content: result.content,
    });
    await vscode.window.showTextDocument(doc);
}
async function cmdTailFile() {
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
    const client = (0, agentClient_1.createAgentClient)();
    const result = await client.tailFile({ path, lines });
    const doc = await vscode.workspace.openTextDocument({
        language: "log",
        content: result.content,
    });
    await vscode.window.showTextDocument(doc);
}
async function cmdGetElementState() {
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
    const client = (0, agentClient_1.createAgentClient)();
    const result = await client.getElementState({ pid, elementId });
    vscode.window.showInformationMessage(`Element ${result.id}: ${result.type} '${result.name}'`);
}
async function cmdWaitForElementCommand() {
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
    const client = (0, agentClient_1.createAgentClient)();
    const result = await client.waitForElement({
        pid,
        elementId,
        timeoutMilliseconds,
    });
    vscode.window.showInformationMessage(result.found
        ? `Element '${elementId}' became available.`
        : `Element '${elementId}' was not found before timeout.`);
}
// ─── Copilot tool implementations ────────────────────────────────────────────
function uiTreeSummary(elements, depth = 0) {
    return elements
        .map((el) => {
        const indent = "  ".repeat(depth);
        const bounds = el.bounds
            ? ` [${el.bounds.x},${el.bounds.y} ${el.bounds.width}×${el.bounds.height}]`
            : "";
        const line = `${indent}${el.type} "${el.name}" id=${el.id}${bounds}`;
        const children = el.children && el.children.length > 0
            ? "\n" + uiTreeSummary(el.children, depth + 1)
            : "";
        return line + children;
    })
        .join("\n");
}
class RunExecutableTool {
    async invoke(options, _token) {
        const { command, arguments: args, workingDirectory } = options.input;
        const request = { command, arguments: args, workingDirectory };
        const client = (0, agentClient_1.createAgentClient)();
        const result = await client.runExecutable(request);
        return new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(`Executable started.\n- PID: ${result.pid}\n- Status: ${result.status}\n- Started at: ${result.startedAt}`),
        ]);
    }
}
class GetUiTreeTool {
    async invoke(options, _token) {
        const client = (0, agentClient_1.createAgentClient)();
        const tree = await client.getUiTree(options.input.pid);
        const summary = uiTreeSummary(tree.elements);
        return new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(`Window: "${tree.windowTitle}"\n\nUI elements:\n${summary}`),
        ]);
    }
}
class GetScreenshotTool {
    async invoke(options, _token) {
        const client = (0, agentClient_1.createAgentClient)();
        const screenshot = await client.getScreenshot(options.input.pid);
        const imageBytes = new Uint8Array(Buffer.from(screenshot.imageBase64, "base64"));
        return new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(`Screenshot captured for process ${options.input.pid}.`),
            vscode.LanguageModelDataPart.image(imageBytes, "image/png"),
        ]);
    }
}
class ClickElementTool {
    async invoke(options, _token) {
        const { pid, elementId } = options.input;
        const client = (0, agentClient_1.createAgentClient)();
        const result = await client.clickElement(pid, elementId);
        const text = result.status === "ok"
            ? `Successfully clicked element '${elementId}'.`
            : `Failed to click element '${elementId}': ${result.message ?? "unknown error"}`;
        return new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(text),
        ]);
    }
}
class TypeTextTool {
    async invoke(options, _token) {
        const { pid, elementId, text } = options.input;
        const client = (0, agentClient_1.createAgentClient)();
        const result = await client.typeText(pid, elementId, text);
        const msg = result.status === "ok"
            ? `Successfully typed text into element '${elementId}'.`
            : `Failed to type text into element '${elementId}': ${result.message ?? "unknown error"}`;
        return new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(msg),
        ]);
    }
}
class CopyFileTool {
    async invoke(options, _token) {
        const { localFilePath, destinationPath, overwriteIfExists } = options.input;
        const fileUri = vscode.Uri.file(localFilePath);
        const fileBytes = await vscode.workspace.fs.readFile(fileUri);
        const base64Content = Buffer.from(fileBytes).toString("base64");
        const client = (0, agentClient_1.createAgentClient)();
        const result = await client.copyFile({
            destinationPath,
            fileContentBase64: base64Content,
            overwriteIfExists: overwriteIfExists ?? true,
        });
        return new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(`File copied to ${result.destinationPath} (${result.bytesWritten} bytes)`),
        ]);
    }
}
class GetProcessStatusTool {
    async invoke(options, _token) {
        const client = (0, agentClient_1.createAgentClient)();
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
class WaitForExitTool {
    async invoke(options, _token) {
        const client = (0, agentClient_1.createAgentClient)();
        const timeoutMilliseconds = options.input.timeoutMilliseconds ?? 30000;
        const result = await client.waitForExit({
            pid: options.input.pid,
            timeoutMilliseconds,
        });
        return new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(result.exited
                ? `Process ${result.process.pid} exited with status ${result.process.status}.`
                : `Process ${result.process.pid} is still running after ${timeoutMilliseconds}ms.`),
        ]);
    }
}
class TerminateProcessTool {
    async invoke(options, _token) {
        const client = (0, agentClient_1.createAgentClient)();
        const result = await client.terminateProcess({ pid: options.input.pid });
        return new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(result.message ?? `Process ${options.input.pid} terminated.`),
        ]);
    }
}
class ReadTextFileTool {
    async invoke(options, _token) {
        const client = (0, agentClient_1.createAgentClient)();
        const result = await client.readTextFile({ path: options.input.path });
        return new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(`File: ${result.path}\n\n${result.content}`),
        ]);
    }
}
class TailFileTool {
    async invoke(options, _token) {
        const client = (0, agentClient_1.createAgentClient)();
        const lines = options.input.lines ?? 200;
        const result = await client.tailFile({
            path: options.input.path,
            lines,
        });
        return new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(`Tail (${result.lines}) ${result.path}\n\n${result.content}`),
        ]);
    }
}
class GetElementStateTool {
    async invoke(options, _token) {
        const client = (0, agentClient_1.createAgentClient)();
        const result = await client.getElementState(options.input);
        return new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(`Element ${result.id}\nType: ${result.type}\nName: ${result.name}\nAvailable: ${result.available}`),
        ]);
    }
}
class WaitForElementUiTool {
    async invoke(options, _token) {
        const client = (0, agentClient_1.createAgentClient)();
        const result = await client.waitForElement({
            pid: options.input.pid,
            elementId: options.input.elementId,
            timeoutMilliseconds: options.input.timeoutMilliseconds ?? 30000,
            pollIntervalMilliseconds: options.input.pollIntervalMilliseconds,
        });
        return new vscode.LanguageModelToolResult([
            new vscode.LanguageModelTextPart(result.found
                ? `Element '${options.input.elementId}' is available.`
                : `Element '${options.input.elementId}' was not found before timeout.`),
        ]);
    }
}
//# sourceMappingURL=extension.js.map