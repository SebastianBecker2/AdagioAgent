import { beforeEach, describe, expect, it, vi } from "vitest";

const {
  commandHandlers,
  toolHandlers,
  showInputBoxMock,
  showErrorMessageMock,
  showInformationMessageMock,
  withProgressMock,
  openTextDocumentMock,
  showTextDocumentMock,
  writeFileMock,
  executeCommandMock,
  registerCommandMock,
  registerToolMock,
  createAgentClientMock,
  LanguageModelTextPart,
  LanguageModelToolResult,
  LanguageModelDataPart,
} = vi.hoisted(() => {
  const hoistedCommandHandlers = new Map<string, (...args: unknown[]) => Promise<void> | void>();
  const hoistedToolHandlers = new Map<string, { invoke: (...args: unknown[]) => Promise<unknown> }>();
  const hoistedRegisterCommandMock = vi.fn((name: string, cb: (...args: unknown[]) => Promise<void> | void) => {
    hoistedCommandHandlers.set(name, cb);
    return { dispose: vi.fn() };
  });
  const hoistedRegisterToolMock = vi.fn((name: string, tool: { invoke: (...args: unknown[]) => Promise<unknown> }) => {
    hoistedToolHandlers.set(name, tool);
    return { dispose: vi.fn() };
  });

  class HoistedLanguageModelTextPart {
    value: string;

    constructor(value: string) {
      this.value = value;
    }
  }

  class HoistedLanguageModelToolResult {
    parts: unknown[];

    constructor(parts: unknown[]) {
      this.parts = parts;
    }
  }

  class HoistedLanguageModelDataPart {
    value: Uint8Array;
    mime: string;

    constructor(value: Uint8Array, mime: string) {
      this.value = value;
      this.mime = mime;
    }

    static image(data: Uint8Array, mime: string) {
      return new HoistedLanguageModelDataPart(data, mime);
    }
  }

  return {
    commandHandlers: hoistedCommandHandlers,
    toolHandlers: hoistedToolHandlers,
    showInputBoxMock: vi.fn(),
    showErrorMessageMock: vi.fn(),
    showInformationMessageMock: vi.fn(),
    withProgressMock: vi.fn(async (_opts, task) => task()),
    openTextDocumentMock: vi.fn(),
    showTextDocumentMock: vi.fn(),
    writeFileMock: vi.fn(),
    executeCommandMock: vi.fn(),
    registerCommandMock: hoistedRegisterCommandMock,
    registerToolMock: hoistedRegisterToolMock,
    createAgentClientMock: vi.fn(),
    LanguageModelTextPart: HoistedLanguageModelTextPart,
    LanguageModelToolResult: HoistedLanguageModelToolResult,
    LanguageModelDataPart: HoistedLanguageModelDataPart,
  };
});

vi.mock("vscode", () => ({
  commands: {
    registerCommand: registerCommandMock,
    executeCommand: executeCommandMock,
  },
  workspace: {
    openTextDocument: openTextDocumentMock,
    fs: {
      writeFile: writeFileMock,
    },
  },
  window: {
    showInputBox: showInputBoxMock,
    showErrorMessage: showErrorMessageMock,
    showInformationMessage: showInformationMessageMock,
    withProgress: withProgressMock,
    showTextDocument: showTextDocumentMock,
  },
  ProgressLocation: {
    Notification: 15,
  },
  Uri: {
    file: (p: string) => ({ fsPath: p }),
    joinPath: (...parts: Array<{ fsPath?: string } | string>) => {
      const normalized = parts
        .map((p) => (typeof p === "string" ? p : p.fsPath ?? ""))
        .join("/");
      return { fsPath: normalized };
    },
  },
  lm: {
    registerTool: registerToolMock,
  },
  LanguageModelTextPart,
  LanguageModelToolResult,
  LanguageModelDataPart,
}));

vi.mock("../src/agentClient", () => ({
  createAgentClient: createAgentClientMock,
  AgentClient: class AgentClient {},
}));

import { activate } from "../src/extension";

describe("extension activation and commands", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    commandHandlers.clear();
    toolHandlers.clear();
    openTextDocumentMock.mockResolvedValue({});
    showTextDocumentMock.mockResolvedValue(undefined);
    writeFileMock.mockResolvedValue(undefined);
    executeCommandMock.mockResolvedValue(undefined);
  });

  it("registers all commands and tools on activate", () => {
    const context = { subscriptions: [] as Array<{ dispose: () => void }> };

    activate(context as never);

    expect(registerCommandMock).toHaveBeenCalledTimes(22);
    expect(commandHandlers.has("adagioAgent.runExecutable")).toBe(true);
    expect(commandHandlers.has("adagioAgent.runInstallerAndCollectArtifacts")).toBe(true);
    expect(commandHandlers.has("adagioAgent.getUiTree")).toBe(true);
    expect(commandHandlers.has("adagioAgent.clickElement")).toBe(true);
    expect(commandHandlers.has("adagioAgent.getScreenshot")).toBe(true);
    expect(commandHandlers.has("adagioAgent.typeText")).toBe(true);
    expect(commandHandlers.has("adagioAgent.copyFile")).toBe(true);
    expect(commandHandlers.has("adagioAgent.getProcessStatus")).toBe(true);
    expect(commandHandlers.has("adagioAgent.waitForExit")).toBe(true);
    expect(commandHandlers.has("adagioAgent.collectInstallArtifacts")).toBe(true);
    expect(commandHandlers.has("adagioAgent.terminateProcess")).toBe(true);
    expect(commandHandlers.has("adagioAgent.readTextFile")).toBe(true);
    expect(commandHandlers.has("adagioAgent.tailFile")).toBe(true);
    expect(commandHandlers.has("adagioAgent.listDirectory")).toBe(true);
    expect(commandHandlers.has("adagioAgent.fileExists")).toBe(true);
    expect(commandHandlers.has("adagioAgent.getElementState")).toBe(true);
    expect(commandHandlers.has("adagioAgent.waitForElement")).toBe(true);
    expect(commandHandlers.has("adagioAgent.setFocus")).toBe(true);
    expect(commandHandlers.has("adagioAgent.sendKeys")).toBe(true);
    expect(commandHandlers.has("adagioAgent.pressHotkey")).toBe(true);
    expect(commandHandlers.has("adagioAgent.setCheckbox")).toBe(true);
    expect(commandHandlers.has("adagioAgent.selectOption")).toBe(true);

    expect(registerToolMock).toHaveBeenCalledTimes(22);
    expect(toolHandlers.has("adagioAgent_runExecutable")).toBe(true);
    expect(toolHandlers.has("adagioAgent_runInstallerAndCollectArtifacts")).toBe(true);
    expect(toolHandlers.has("adagioAgent_getUiTree")).toBe(true);
    expect(toolHandlers.has("adagioAgent_getScreenshot")).toBe(true);
    expect(toolHandlers.has("adagioAgent_clickElement")).toBe(true);
    expect(toolHandlers.has("adagioAgent_typeText")).toBe(true);
    expect(toolHandlers.has("adagioAgent_copyFile")).toBe(true);
    expect(toolHandlers.has("adagioAgent_getProcessStatus")).toBe(true);
    expect(toolHandlers.has("adagioAgent_waitForExit")).toBe(true);
    expect(toolHandlers.has("adagioAgent_collectInstallArtifacts")).toBe(true);
    expect(toolHandlers.has("adagioAgent_terminateProcess")).toBe(true);
    expect(toolHandlers.has("adagioAgent_readTextFile")).toBe(true);
    expect(toolHandlers.has("adagioAgent_tailFile")).toBe(true);
    expect(toolHandlers.has("adagioAgent_listDirectory")).toBe(true);
    expect(toolHandlers.has("adagioAgent_fileExists")).toBe(true);
    expect(toolHandlers.has("adagioAgent_getElementState")).toBe(true);
    expect(toolHandlers.has("adagioAgent_waitForElement")).toBe(true);
    expect(toolHandlers.has("adagioAgent_setFocus")).toBe(true);
    expect(toolHandlers.has("adagioAgent_sendKeys")).toBe(true);
    expect(toolHandlers.has("adagioAgent_pressHotkey")).toBe(true);
    expect(toolHandlers.has("adagioAgent_setCheckbox")).toBe(true);
    expect(toolHandlers.has("adagioAgent_selectOption")).toBe(true);
  });

  it("getUiTree command rejects invalid pid without calling API", async () => {
    const context = { subscriptions: [] as Array<{ dispose: () => void }> };
    activate(context as never);

    showInputBoxMock.mockResolvedValueOnce("abc");

    const handler = commandHandlers.get("adagioAgent.getUiTree");
    await handler?.();

    expect(showErrorMessageMock).toHaveBeenCalledWith("Invalid PID.");
    expect(createAgentClientMock).not.toHaveBeenCalled();
  });

  it("click/screenshot/type commands reject invalid pid", async () => {
    const context = { subscriptions: [] as Array<{ dispose: () => void }> };
    activate(context as never);

    showInputBoxMock.mockResolvedValueOnce("-4");
    await commandHandlers.get("adagioAgent.clickElement")?.();

    showInputBoxMock.mockResolvedValueOnce("0");
    await commandHandlers.get("adagioAgent.getScreenshot")?.();

    showInputBoxMock.mockResolvedValueOnce("nan");
    await commandHandlers.get("adagioAgent.typeText")?.();

    expect(showErrorMessageMock).toHaveBeenCalledTimes(3);
    expect(createAgentClientMock).not.toHaveBeenCalled();
  });

  it("GetUiTree tool renders recursive summary with bounds", async () => {
    const context = { subscriptions: [] as Array<{ dispose: () => void }> };
    createAgentClientMock.mockReturnValue({
      getUiTree: vi.fn().mockResolvedValue({
        windowTitle: "Calculator",
        elements: [
          {
            id: "button-seven",
            type: "button",
            name: "7",
            automationId: "Seven",
            bounds: { x: 10, y: 20, width: 30, height: 40 },
            children: [{
              id: "text-child",
              type: "text",
              name: "child",
              automationId: "",
            }],
          },
        ],
      }),
    });

    activate(context as never);

    const tool = toolHandlers.get("adagioAgent_getUiTree");
    const result = await tool?.invoke({ input: { pid: 42 } }, {});
    const textPart = (result as { parts: Array<{ value: string }> }).parts[0];

    expect(textPart.value).toContain('Window: "Calculator"');
    expect(textPart.value).toContain('button "7" id=button-seven [10,20 30×40]');
    expect(textPart.value).toContain('  text "child" id=text-child');
  });

  it("GetScreenshot tool returns an image data part", async () => {
    const context = { subscriptions: [] as Array<{ dispose: () => void }> };
    createAgentClientMock.mockReturnValue({
      getScreenshot: vi.fn().mockResolvedValue({
        imageBase64: Buffer.from("png-bytes").toString("base64"),
      }),
    });

    activate(context as never);

    const tool = toolHandlers.get("adagioAgent_getScreenshot");
    const result = await tool?.invoke({ input: { pid: 42 } }, {});
    const parts = (result as { parts: Array<{ value?: string; mime?: string }> }).parts;

    expect(parts).toHaveLength(2);
    expect(parts[0].value).toContain("Screenshot captured for process 42.");
    expect(parts[1].mime).toBe("image/png");
  });
});
