import { describe, expect, it, vi } from "vitest";

const { showErrorMessageMock } = vi.hoisted(() => ({
  showErrorMessageMock: vi.fn(),
}));

vi.mock("vscode", () => ({
  window: {
    showErrorMessage: showErrorMessageMock,
  },
}));

import { wrapCommand } from "../src/commandSafety";

describe("wrapCommand", () => {
  it("executes wrapped command successfully", async () => {
    const handler = vi.fn(async () => undefined);
    const wrapped = wrapCommand(handler);

    await wrapped();

    expect(handler).toHaveBeenCalledTimes(1);
    expect(showErrorMessageMock).not.toHaveBeenCalled();
  });

  it("shows user-friendly error message when command throws", async () => {
    const wrapped = wrapCommand(async () => {
      throw new Error("broken config");
    });

    await wrapped();

    expect(showErrorMessageMock).toHaveBeenCalledWith(
      "Adagio Agent command failed: broken config"
    );
  });

  it("handles non-Error throw values", async () => {
    const wrapped = wrapCommand(async () => {
      throw "plain string";
    });

    await wrapped();

    expect(showErrorMessageMock).toHaveBeenCalledWith(
      "Adagio Agent command failed: plain string"
    );
  });
});
