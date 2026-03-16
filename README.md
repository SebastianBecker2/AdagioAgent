# AdagioAgent

Automated execution harness connecting GitHub Copilot (via a VS Code
extension) to a UI-automation agent running inside a Windows VM.

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│  Developer machine                                                      │
│                                                                         │
│  ┌──────────────────────────────────┐                                   │
│  │  VS Code + Copilot               │                                   │
│  │  controller-extension/           │                                   │
│  │  • extension.ts  (commands)      │  REST (HTTP)                      │
│  │  • agentClient.ts (HTTP client)  │ ──────────────────────────────►  │
│  │  • schema.ts     (types)         │                                   │
│  └──────────────────────────────────┘                                   │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
                                                        │
                                              ┌─────────▼───────────────────┐
                                              │  Windows VM                 │
                                              │  machine-agent/                  │
                                              │  • Program.cs               │
                                              │  • AutomationController.cs  │
                                              │  • ProcessService.cs        │
                                              │  • UiAutomationService.cs   │
                                              └─────────────────────────────┘
```

---

## Components

### `controller-extension/` — VS Code Extension (TypeScript)

Exposes five Copilot language-model tools and matching VS Code commands:

| Tool / Command | Description |
|---|---|
| `adagioAgent_runExecutable` | Start an executable on the VM |
| `adagioAgent_getUiTree` | Dump the UI element hierarchy |
| `adagioAgent_getScreenshot` | Capture a screenshot |
| `adagioAgent_clickElement` | Click a UI element by ID |
| `adagioAgent_typeText` | Type text into a UI element |

**Build:**

```bash
cd controller-extension
npm install
npm run compile
```

**Configuration** (`.vscode/settings.json` or VS Code UI):

| Setting | Default | Description |
|---|---|---|
| `adagioAgent.vmAgentUrl` | `http://localhost:5000` | VM agent base URL |
| `adagioAgent.allowedExecutablePaths` | `["C:\\Apps"]` | Command whitelist |

---

### `machine-agent/` — Windows VM Agent (.NET 8, C#)

Minimal Kestrel web host exposing a REST API for process control and UI
automation via **FlaUI (UIA3)**.

**REST API:**

| Method | Path | Description |
|---|---|---|
| `GET` | `/health` | Health check |
| `POST` | `/run` | Start an executable process |
| `GET` | `/ui-tree?pid=N` | Dump UI element tree |
| `GET` | `/screenshot?pid=N` | Capture window screenshot (base64 PNG) |
| `POST` | `/click` | Click a UI element |
| `POST` | `/type` | Type text into a UI element |

**Build (cross-platform):**

```bash
cd machine-agent
dotnet build
```

**Run (Windows only):**

```bash
dotnet run
```

Listens on `http://0.0.0.0:5000` by default (see `appsettings.json`).

**Safety guardrails:**

- **Command whitelist** — only paths under `AgentOptions.AllowedExecutablePaths`
  are allowed; all others are rejected with HTTP 400.
- **Process timeout** — processes are forcibly killed after
  `AgentOptions.ProcessTimeoutSeconds` (default 300 s).
- **Concurrency limit** — at most `AgentOptions.MaxConcurrentProcesses`
  (default 5) concurrent processes are allowed.

---

## Typical flow

1. Copilot calls **`adagioAgent_runExecutable`** → VS Code sends `POST /run` → agent starts the executable, returns PID.
2. Copilot calls **`adagioAgent_getUiTree`** → VS Code sends `GET /ui-tree?pid=…` → agent returns element tree.
3. Copilot reasons: *"I should click the Next button"* → calls **`adagioAgent_clickElement`** → VS Code sends `POST /click`.
4. Repeat until the application exits.
