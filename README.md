# AdagioAgent

[![AppVeyor Build Status](https://ci.appveyor.com/api/projects/status/github/SebastianBecker2/AdagioAgent?branch=main&svg=true)](https://ci.appveyor.com/project/SebastianBecker2/AdagioAgent)

Automated execution harness connecting GitHub Copilot (via a VS Code
extension) to a UI-automation agent running inside a Windows or Linux VM.

> Note: This project is AI-generated.

Current product posture: Windows-first, admin-managed deployment for controlled
environments. See [docs/OPERATING_MODEL.md](docs/OPERATING_MODEL.md) for the
current support boundaries and deployment assumptions, and
[docs/BOOTSTRAP_STRATEGY.md](docs/BOOTSTRAP_STRATEGY.md) for the provisioning
strategy decision. Troubleshooting workflows are documented in
[docs/DIAGNOSTICS_TROUBLESHOOTING.md](docs/DIAGNOSTICS_TROUBLESHOOTING.md).

Governance and support docs:

- [CHANGELOG.md](CHANGELOG.md)
- [SECURITY.md](SECURITY.md)
- [SUPPORT.md](SUPPORT.md)
- [CONTRIBUTING.md](CONTRIBUTING.md)

Pilot-readiness docs:

- [docs/PILOT_RUNBOOK.md](docs/PILOT_RUNBOOK.md)
- [docs/ROLLBACK_CHECKLIST.md](docs/ROLLBACK_CHECKLIST.md)
- [docs/UPGRADE_VALIDATION_CHECKLIST.md](docs/UPGRADE_VALIDATION_CHECKLIST.md)
- [docs/SUPPORT_BUNDLE_SCHEMA.md](docs/SUPPORT_BUNDLE_SCHEMA.md)
- [docs/RELEASE_SUPPORT_QUICKSTART.md](docs/RELEASE_SUPPORT_QUICKSTART.md)
- [docs/OPERATIONS_SIGNOFF_TEMPLATE.md](docs/OPERATIONS_SIGNOFF_TEMPLATE.md)

Observability docs:

- [docs/OBSERVABILITY_FIELDS.md](docs/OBSERVABILITY_FIELDS.md)

Bootstrap helper script: `scripts/bootstrap-agent.ps1` (certificate + API key
generation for controlled environments).

Support bundle helper script: `scripts/collect-support-bundle.ps1`
(sanitized diagnostics and operational evidence collection).

Use `-ExtensionOutputPath` with the support-bundle script when you want the
bundle to include extension diagnostics export path metadata.

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
                                              │  Windows VM  OR  Linux VM   │
                                              │  machine-agent/             │
                                              │  • Program.cs               │
                                              │  • AutomationController.cs  │
                                              │  • ProcessService.cs        │
                                              │  • IUiAutomationService     │
                                              │    (Windows: FlaUI/UIA3)    │
                                              │    (Linux: AT-SPI2/X11)     │
                                              └─────────────────────────────┘
```

---

## Components

### `controller-extension/` — VS Code Extension (TypeScript)

Exposes a generalized set of Copilot language-model tools and matching VS Code
commands for process execution, diagnostics, file transfer, and UI automation.

Core workflow tools (generalized names):

| Tool / Command | Description |
|---|---|
| `adagioAgent_runExecutable` | Start an executable on the target machine |
| `adagioAgent_runAndCollectArtifacts` | Start a process and collect exit/log/event diagnostics |
| `adagioAgent_runAndAssert` | Start a process, collect artifacts, and evaluate assertions |
| `adagioAgent_collectProcessArtifacts` | Collect diagnostics for an existing tracked process |

Interaction and assertion tools:

| Tool / Command | Description |
|---|---|
| `adagioAgent_copyFile` | Copy a local file to the target machine |
| `adagioAgent_getProcessStatus` | Query tracked process status |
| `adagioAgent_waitForExit` | Wait for a process to exit |
| `adagioAgent_assertProcessExited` | Assert process exit/exit-code expectations |
| `adagioAgent_assertPathExists` | Assert file/directory existence |
| `adagioAgent_assertLogContains` | Assert text in log/file content |
| `adagioAgent_readTextFile` | Read a UTF-8 text file |
| `adagioAgent_tailFile` | Read tail lines from a text file |
| `adagioAgent_listDirectory` | Enumerate file system entries |
| `adagioAgent_fileExists` | Check file/directory existence |
| `adagioAgent_getUiTree` | Dump the UI element hierarchy |
| `adagioAgent_getElementState` | Inspect one UI element state |
| `adagioAgent_waitForElement` | Wait until UI element becomes available |
| `adagioAgent_getScreenshot` | Capture a screenshot |
| `adagioAgent_clickElement` | Click a UI element by ID |
| `adagioAgent_typeText` | Type text into a UI element |
| `adagioAgent_setFocus` | Focus a UI element |
| `adagioAgent_sendKeys` | Send keystrokes to app window |
| `adagioAgent_pressHotkey` | Press key combinations |
| `adagioAgent_setCheckbox` | Toggle checkbox/radio controls |
| `adagioAgent_selectOption` | Select combo/list options |

Installer-named tools remain available as compatibility aliases:

- `adagioAgent_runInstallerAndCollectArtifacts` (alias of run-and-collect workflow)
- `adagioAgent_runInstallerAndAssert` (alias of run-and-assert workflow)
- `adagioAgent_collectInstallArtifacts` (alias of collect-process-artifacts)

Operational command:

- `adagioAgent.runStartupDiagnostics` (rerun readiness diagnostics on demand)
- `adagioAgent.openDiagnosticsOutput` (open extension diagnostics output and current readiness summary)

**Build:**

```bash
cd controller-extension
npm install
npm run compile
```

**Configuration** (`.vscode/settings.json` or VS Code UI):

| Setting | Default | Description |
|---|---|---|
| `adagioAgent.vmAgentUrl` | `https://127.0.0.1:5443` | VM agent base URL |
| `adagioAgent.vmAgentApiKey` | `""` | API key sent in `X-API-Key` header |
| `adagioAgent.requireHttps` | `true` | Reject non-HTTPS `vmAgentUrl` values |
| `adagioAgent.allowedExecutablePaths` | `["C:\\Apps"]` | Command whitelist |

---

### `machine-agent/` — VM Agent (.NET 8, C#)

Minimal Kestrel web host exposing a REST API for process control and UI
automation. Supports **Windows** (via **FlaUI/UIA3**) and **Linux with a GUI**
(via **AT-SPI2** and **X11**).

**REST API:**

| Method | Path | Description |
|---|---|---|
| `GET` | `/health` | Health check |
| `GET` | `/ready` | Readiness check with actionable configuration/runtime issues |
| `GET` | `/diagnostics/status` | Summarized startup/runtime diagnostics |
| `GET` | `/diagnostics/export-metadata` | Non-sensitive support/export metadata snapshot |
| `POST` | `/run` | Start an executable process |
| `POST` | `/run-and-collect-artifacts` | Start process, wait, collect diagnostics |
| `POST` | `/run-and-assert` | Start process, collect diagnostics, evaluate assertions |
| `GET` | `/process-status?pid=N` | Query tracked process status |
| `POST` | `/wait-for-exit` | Wait for tracked process exit |
| `POST` | `/collect-process-artifacts` | Collect diagnostics for tracked process |
| `POST` | `/terminate` | Terminate tracked process |
| `GET` | `/ui-tree?pid=N` | Dump UI element tree |
| `POST` | `/element-state` | Get state snapshot for one UI element |
| `POST` | `/wait-for-element` | Wait for UI element availability |
| `POST` | `/focus` | Focus a UI element |
| `POST` | `/send-keys` | Send keystrokes |
| `POST` | `/press-hotkey` | Send key combination |
| `POST` | `/set-checkbox` | Set checkbox/radio checked state |
| `POST` | `/select-option` | Select combo/list option |
| `GET` | `/screenshot?pid=N` | Capture window screenshot (base64 PNG) |
| `POST` | `/click` | Click a UI element |
| `POST` | `/type` | Type text into a UI element |
| `POST` | `/copy-file` | Copy file content to target path |
| `POST` | `/read-text-file` | Read full UTF-8 text file |
| `POST` | `/tail-file` | Read tail lines from file |
| `POST` | `/list-directory` | List files/directories |
| `POST` | `/file-exists` | Check path existence/type |
| `POST` | `/assert-process-exited` | Assert process exits (optional exit code) |
| `POST` | `/assert-path-exists` | Assert path exists/type |
| `POST` | `/assert-log-contains` | Assert file contains expected text |

Legacy installer-named route aliases are still supported for backward compatibility:

- `/run-installer-and-collect-artifacts`
- `/run-installer-and-assert`
- `/collect-install-artifacts`

OpenAPI/Swagger is available at `/swagger` with a canonical server base URL of
`/api/v1` for client generation and contract review.

**API error contract (concise):**

Error responses use a consistent JSON shape:

| Field | Type | Description |
|---|---|---|
| `error` | string | High-level failure message |
| `detail` | string? | Optional diagnostic detail |
| `correlationId` | string? | Correlation token that maps user-visible errors to backend request logs |

Example:

```json
{
  "error": "Missing required header 'X-API-Key'.",
  "detail": null,
  "correlationId": "0HNK4PIRJOR0P"
}
```

**Build:**

```bash
cd machine-agent
dotnet build
```

**Run:**

```bash
dotnet run
```

Listens on `https://127.0.0.1:5443` by default (see `appsettings.json`).

By default the agent requires an API key in the `X-API-Key` header on all
requests. Set `SecurityOptions.ApiKey` in `appsettings.json` before use.

The agent also requires an explicit HTTPS certificate by default. Configure:

- `SecurityOptions.HttpsCertificatePath` (path to a `.pfx` file)
- `SecurityOptions.HttpsCertificatePassword`

Startup fails fast with a clear error if HTTPS is required but the certificate
is missing/invalid, or if API key auth is required but the key is unset.

**Platform-specific UI automation backends:**

| Platform | Backend | Notes |
|---|---|---|
| Windows | FlaUI (UIA3) | Built-in Windows UI Automation |
| Linux | AT-SPI2 + X11 | Requires packages listed below |

**Linux prerequisites:**

```bash
# Debian / Ubuntu
sudo apt-get install at-spi2-core libx11-6

# Fedora / RHEL
sudo dnf install at-spi2-core libX11
```

Applications must support AT-SPI2 accessibility (all GTK and Qt applications
do by default; Electron apps require `--force-renderer-accessibility`).

The `DISPLAY` and `DBUS_SESSION_BUS_ADDRESS` environment variables must be set
(they are automatically when running as a graphical desktop session).

**Safety guardrails:**

- **HTTPS certificate fail-fast** - startup fails if `RequireHttps` is enabled
  and the configured certificate path/password are invalid.
- **API key authentication** - every request must include the configured
  `X-API-Key` value (`SecurityOptions.RequireApiKey = true` by default).
- **Correlation IDs** - every response includes `X-Correlation-ID`; provide this
  value in support tickets to connect request/diagnostic logs.
- **Standardized validation/unhandled errors** - API validation and global
  exception paths return a consistent JSON error contract.
- **Command whitelist** — only paths under `AgentOptions.AllowedExecutablePaths`
  are allowed; all others are rejected with HTTP 400.
- **Process timeout** — processes are forcibly killed after
  `AgentOptions.ProcessTimeoutSeconds` (default 300 s).
- **Concurrency limit** — at most `AgentOptions.MaxConcurrentProcesses`
  (default 5) concurrent processes are allowed.

---

### `installer/` — Windows MSI Installer

Produces a self-contained Windows MSI (`AdagioMachineAgentSetup.msi`) that
installs and registers the machine-agent as a Windows Service
(`AdagioMachineAgent`) starting automatically on boot.

**Prerequisites:**

- .NET 8 SDK (used to publish the agent before packaging)
- [WiX Toolset SDK v6.0.2](https://wixtoolset.org/) (downloaded automatically
  as NuGet packages on first build; includes **WixToolset.Heat** for
  directory-based file harvesting)

**Build the installer (run from the repo root or the `installer/` directory):**

```powershell
dotnet build installer/AdagioMachineAgent.Setup.wixproj -p:Configuration=Release
```

The installer is written to:
```
installer/bin/x64/Release/AdagioMachineAgentSetup.msi
```

**What the installer does:**

| Action | Detail |
|---|---|
| Install location | `%ProgramFiles%\AdagioMachineAgent\` |
| Service name | `AdagioMachineAgent` |
| Service display name | `Adagio Machine Agent` |
| Start type | Automatic (starts on boot) |
| Service account | `NT AUTHORITY\LocalService` |
| Listens on | `https://127.0.0.1:5443` |
| Uninstall | Stops & removes the service, deletes all installed files |
| Upgrade | Major-upgrade (replaces earlier versions in-place) |

**Install / Uninstall:**

```powershell
# Install
msiexec /i AdagioMachineAgentSetup.msi /quiet

# Uninstall
msiexec /x AdagioMachineAgentSetup.msi /quiet
```

Or use **Add / Remove Programs** for a GUI experience.

**Configuration after installation:**

Edit `%ProgramFiles%\AdagioMachineAgent\appsettings.json` then restart the
service:

```powershell
Restart-Service AdagioMachineAgent
```

Set these values before starting the service:

- `SecurityOptions.ApiKey`
- `SecurityOptions.HttpsCertificatePath`
- `SecurityOptions.HttpsCertificatePassword`

**UI automation and service account:**

Windows services run in Session 0, which is isolated from the interactive
desktop. For FlaUI/UIA3 UI automation to reach applications running in the
user session, change the service's *Log On* account to the interactive user
account (via `services.msc` or the commands below) and restart the service:

```powershell
# Local account:
sc.exe config AdagioMachineAgent obj= ".\YourUserName" password= "YourPassword"
# Domain account:
sc.exe config AdagioMachineAgent obj= "DOMAIN\YourUserName" password= "YourPassword"
Restart-Service AdagioMachineAgent
```

**Notes:**

- **WiX 6.0.2**: Project upgraded from v4 to v6.0.2 to modernize the installer
  toolchain. Build process is now fully managed by `dotnet build` with
  transparent NuGet Package restoration.
- **Heat (WixToolset.Heat)**: Automatically harvests runtime files from the
  publish directory into the installer. Heat is **deprecated in WiX v7** and
  will be replaced by the native `Files` element for future versions.
- **Build artifact**: MSI is produced at `installer/bin/x64/Release/` and is
  self-contained (69 MB); ready for distribution.

---

## Versioning and Compatibility

### Version scheme

Each artifact follows independent [Semantic Versioning](https://semver.org/):

| Artifact | Current version | Notes |
|---|---|---|
| `controller-extension` | see `controller-extension/package.json` | VSIX / npm |
| `machine-agent` | see `machine-agent/AdagioMachineAgent.csproj` (`<Version>`) | Service binary |
| `installer` | follows machine-agent release | MSI |

### REST API versioning

All endpoints are available under the versioned prefix `/api/v1/...` as well
as their legacy unversioned paths (e.g., `/run`).  The versioned prefix is the
preferred form for all new integrations.

```
# Versioned (preferred):
GET  https://127.0.0.1:5443/api/v1/health
POST https://127.0.0.1:5443/api/v1/run

# Legacy (compatibility aliases — deprecated, will be removed in API v2):
GET  https://127.0.0.1:5443/health
POST https://127.0.0.1:5443/run
```

The `GET /api/v1/health` (and `/health`) response includes the current API
version and the minimum client version the agent supports:

```json
{
  "status": "healthy",
  "version": "0.1.0",
  "apiVersion": 1,
  "minSupportedClientVersion": "0.1.0"
}
```

### Compatibility policy

- The `controller-extension` targets **API major version 1**.
- The `machine-agent` publishes its `apiVersion` in the health response so clients
  can detect incompatibilities at runtime.
- Breaking REST API changes require a new major path (`/api/v2/...`); until
  then, the existing `/api/v1/...` surface is guaranteed stable.
- The `apiVersion` in the health response changes only on breaking API changes.

### Compatibility matrix

| Extension version | Supported agent API versions | Notes |
|---|---|---|
| 0.1.x | `/api/v1` | Current release |

### Deprecation timeline

The unversioned legacy routes (`/health`, `/run`, etc.) are **deprecated** and
will be removed when a `/api/v2` surface is introduced. No removal date is set
yet; they will remain available throughout the API v1 lifecycle.

For the full step-by-step release procedure see [docs/RELEASING.md](docs/RELEASING.md).
For the implementation status of versioning work see [docs/VERSIONING_PLAN.md](docs/VERSIONING_PLAN.md).
For the broader product roadmap see [docs/PRODUCTIZATION_PLAN.md](docs/PRODUCTIZATION_PLAN.md).

---

## CI test reports (AppVeyor)

The AppVeyor pipeline runs both test suites on each build:

- `.NET tests` via `dotnet test AdagioAgent.sln -c Release`
- `Extension tests` via `npm test` in `controller-extension/`

Test results are exported as downloadable AppVeyor artifacts:

| Artifact name | Format | Source path |
|---|---|---|
| `DotNetTestResults` | TRX | `TestResults/**/*.trx` |
| `ExtensionTestResults` | JUnit XML | `controller-extension/test-results/*.xml` |

These are in addition to the installer artifact (`AdagioMachineAgentSetup`).

---

## Typical flow

1. Copy binaries/assets if needed via **`adagioAgent_copyFile`**.
2. Start and track execution via **`adagioAgent_runExecutable`** or the higher-level **`adagioAgent_runAndAssert`** workflow.
3. For GUI apps, inspect and interact using **`adagioAgent_getUiTree`**, **`adagioAgent_clickElement`**, **`adagioAgent_typeText`**, and related UI tools.
4. For CLI/background processes, inspect with **`adagioAgent_getProcessStatus`**, **`adagioAgent_collectProcessArtifacts`**, and assertion tools.
5. Verify outcomes using assertion helpers (**process**, **path**, **log**) and terminate remaining processes if needed.
