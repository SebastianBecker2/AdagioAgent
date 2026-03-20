# Linux UI Command Parity Matrix

This document tracks Linux parity for UI automation commands listed in roadmap
item 2.1 and records how each capability is validated.

## Scope

- Platform: Linux (Ubuntu 22.04/24.04 target)
- Backend: AT-SPI2 + X11
- APIs in scope: `get-ui-tree`, `element-state`, `click`, `type`, `send-keys`, `wait-for-element`, `press-hotkey`

## Command Matrix

| Command | Status | Backend Path | Verification |
|---|---|---|---|
| `GET /ui-tree` | Implemented | `LinuxUiAutomationService.GetUiTree` | Unit/integration tests + Linux CI build/test |
| `POST /element-state` | Implemented | `LinuxUiAutomationService.GetElementState` | Unit/integration tests + Linux CI build/test |
| `POST /click` | Implemented | `LinuxUiAutomationService.Click` | Unit/integration tests + Linux CI build/test |
| `POST /type` | Implemented | `LinuxUiAutomationService.Type` | Unit/integration tests + Linux CI build/test |
| `POST /send-keys` | Implemented | `LinuxUiAutomationService.SendKeys` (`xdotool type`) | Linux UI parity smoke (`scripts/linux-ui-parity-smoke.sh`) |
| `POST /wait-for-element` | Implemented | `LinuxUiAutomationService.WaitForElement` | Unit tests + Linux CI build/test |
| `POST /press-hotkey` | Implemented | `LinuxUiAutomationService.PressHotkey` (`xdotool key`) | Linux UI parity smoke (`scripts/linux-ui-parity-smoke.sh`) |

## CI Evidence

Linux parity evidence is produced by the Linux CI workflow:

- Workflow: `.github/workflows/linux-ci.yml`
- Artifact: `linux-ui-parity-smoke` (`linux-ui-parity-smoke.txt`)
- Script: `scripts/linux-ui-parity-smoke.sh`

## Current Constraints

- `send-keys` and `press-hotkey` require X11-compatible input tooling (`xdotool`).
- Pure Wayland sessions without XWayland compatibility are not yet in parity scope.
- AT-SPI2 automation still requires a live graphical session.

## Exit Criteria for Phase 2.1 Completion

Phase 2.1 is considered complete when:

1. All commands in the matrix are marked `Implemented`.
2. Linux CI passes with both:
   - standard machine-agent tests, and
   - Linux UI parity smoke artifact generation.
3. `docs/OPERATING_MODEL.md` and `docs/plans/PROJECT_ROADMAP.md` remain aligned with this matrix.
