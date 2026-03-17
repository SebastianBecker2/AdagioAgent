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

    expect(registerCommandMock).toHaveBeenCalledTimes(5);
    expect(commandHandlers.has("adagioAgent.runExecutable")).toBe(true);
    expect(commandHandlers.has("adagioAgent.getUiTree")).toBe(true);
    expect(commandHandlers.has("adagioAgent.clickElement")).toBe(true);
    expect(commandHandlers.has("adagioAgent.getScreenshot")).toBe(true);
    expect(commandHandlers.has("adagioAgent.typeText")).toBe(true);

    expect(registerToolMock).toHaveBeenCalledTimes(5);
    expect(toolHandlers.has("adagioAgent_runExecutable")).toBe(true);
    expect(toolHandlers.has("adagioAgent_getUiTree")).toBe(true);
    expect(toolHandlers.has("adagioAgent_getScreenshot")).toBe(true);
    expect(toolHandlers.has("adagioAgent_clickElement")).toBe(true);
    expect(toolHandlers.has("adagioAgent_typeText")).toBe(true);
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
});
