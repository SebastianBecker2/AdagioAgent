import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("vscode", () => ({
  window: {
    showErrorMessage: vi.fn(),
  },
}));

import { wrapCommand } from "../src/commandSafety";

describe("wrapCommand", () => {
  const showErrorMessageMock = vi.fn();

  beforeEach(() => {
    showErrorMessageMock.mockReset();
  });

  it("executes wrapped command successfully", async () => {
    const handler = vi.fn(async () => undefined);
    const wrapped = wrapCommand(handler, showErrorMessageMock);

    await wrapped();

    expect(handler).toHaveBeenCalledTimes(1);
    expect(showErrorMessageMock).not.toHaveBeenCalled();
  });

  it("shows user-friendly error message when command throws", async () => {
    const wrapped = wrapCommand(async () => {
      throw new Error("broken config");
    }, showErrorMessageMock);

    await wrapped();

    expect(showErrorMessageMock).toHaveBeenCalledWith(
      "Adagio Agent command failed: broken config"
    );
  });

  it("handles non-Error throw values", async () => {
    const wrapped = wrapCommand(async () => {
      throw "plain string";
    }, showErrorMessageMock);

    await wrapped();

    expect(showErrorMessageMock).toHaveBeenCalledWith(
      "Adagio Agent command failed: plain string"
    );
  });
});
