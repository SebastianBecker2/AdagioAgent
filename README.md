# AdagioAgent

[![AppVeyor Build Status](https://ci.appveyor.com/api/projects/status/github/SebastianBecker2/AdagioAgent?branch=main&svg=true)](https://ci.appveyor.com/project/SebastianBecker2/AdagioAgent)

Automated execution harness connecting GitHub Copilot (via a VS Code
extension) to a UI-automation agent running inside a Windows or Linux VM.

> Note: This project is AI-generated.

---

## Choose your path

| I want to... | Start here |
|---|---|
| Use AdagioAgent on a VM | [docs/QUICKSTART.md](docs/QUICKSTART.md) |
| Install the VS Code extension from release artifacts | [docs/QUICKSTART.md](docs/QUICKSTART.md#step-2--install-the-vs-code-extension) |
| Troubleshoot readiness, bootstrap, or support bundles | [docs/DIAGNOSTICS_TROUBLESHOOTING.md](docs/DIAGNOSTICS_TROUBLESHOOTING.md) |
| Understand product direction | [docs/PROJECT_ROADMAP.md](docs/PROJECT_ROADMAP.md) |
| Develop or modify the codebase | [CONTRIBUTING.md](CONTRIBUTING.md) |

---

## I want to use AdagioAgent

**New here?** → Follow the [Quick-start guide](docs/QUICKSTART.md) to go from zero to your first Copilot automation command in under 10 minutes.

### What you install

- **Machine agent on the target VM:**
  - Windows: install `AdagioMachineAgentSetup.msi` from [GitHub Releases](https://github.com/SebastianBecker2/AdagioAgent/releases)
  - Linux: install `adagio-machine-agent_*.deb` from [GitHub Releases](https://github.com/SebastianBecker2/AdagioAgent/releases)
- **VS Code extension on your workstation:** install **Adagio Agent Controller** from the Marketplace when available, or from a `.vsix` release asset today.

### Fast path

1. Install the machine agent on the VM.
2. Install the VS Code extension.
3. Set `adagioAgent.vmAgentUrl` and `adagioAgent.vmAgentApiKey` in VS Code.
4. Run **Adagio Agent: Run Startup Diagnostics**.
5. Use Copilot Chat to invoke Adagio tools.

**Configure** the extension with your VM's URL and API key (see [docs/QUICKSTART.md](docs/QUICKSTART.md) Step 3).

### Operator docs

- [docs/QUICKSTART.md](docs/QUICKSTART.md)
- [docs/PILOT_RUNBOOK.md](docs/PILOT_RUNBOOK.md)
- [docs/ROLLBACK_CHECKLIST.md](docs/ROLLBACK_CHECKLIST.md)
- [docs/UPGRADE_VALIDATION_CHECKLIST.md](docs/UPGRADE_VALIDATION_CHECKLIST.md)
- [docs/RELEASE_SUPPORT_QUICKSTART.md](docs/RELEASE_SUPPORT_QUICKSTART.md)
- [docs/SUPPORT_BUNDLE_SCHEMA.md](docs/SUPPORT_BUNDLE_SCHEMA.md)
- [docs/OPERATIONS_SIGNOFF_TEMPLATE.md](docs/OPERATIONS_SIGNOFF_TEMPLATE.md)
- [docs/release-ops/README.md](docs/release-ops/README.md)

---

## I want to develop AdagioAgent

Current product posture: Windows-first, admin-managed deployment for controlled
environments. See [docs/OPERATING_MODEL.md](docs/OPERATING_MODEL.md) for the
current support boundaries and deployment assumptions, and
[docs/BOOTSTRAP_STRATEGY.md](docs/BOOTSTRAP_STRATEGY.md) for the provisioning
strategy decision. Troubleshooting workflows are documented in
[docs/DIAGNOSTICS_TROUBLESHOOTING.md](docs/DIAGNOSTICS_TROUBLESHOOTING.md).

Project direction:

- [docs/PROJECT_ROADMAP.md](docs/PROJECT_ROADMAP.md)

### Governance and support

- [CHANGELOG.md](CHANGELOG.md)
- [SECURITY.md](SECURITY.md)
- [SUPPORT.md](SUPPORT.md)
- [CONTRIBUTING.md](CONTRIBUTING.md)

### Delivery and readiness docs

- [docs/PILOT_RUNBOOK.md](docs/PILOT_RUNBOOK.md)
- [docs/ROLLBACK_CHECKLIST.md](docs/ROLLBACK_CHECKLIST.md)
- [docs/UPGRADE_VALIDATION_CHECKLIST.md](docs/UPGRADE_VALIDATION_CHECKLIST.md)
- [docs/SUPPORT_BUNDLE_SCHEMA.md](docs/SUPPORT_BUNDLE_SCHEMA.md)
- [docs/RELEASE_SUPPORT_QUICKSTART.md](docs/RELEASE_SUPPORT_QUICKSTART.md)
- [docs/OPERATIONS_SIGNOFF_TEMPLATE.md](docs/OPERATIONS_SIGNOFF_TEMPLATE.md)
- [docs/release-ops/README.md](docs/release-ops/README.md)

### Observability docs

- [docs/OBSERVABILITY_FIELDS.md](docs/OBSERVABILITY_FIELDS.md)

### Common developer commands

Bootstrap helper script: `scripts/bootstrap-agent.ps1` (certificate + API key
generation for controlled environments).

Bootstrap script regression tests: `Invoke-Pester -Path .\scripts\tests\BootstrapScripts.Tests.ps1`.

Installer validation matrix: `powershell -ExecutionPolicy Bypass -File .\scripts\test-installer-bootstrap-matrix.ps1 -MsiPath .\installer\bin\x64\Release\AdagioMachineAgentSetup.msi -FailOnScenarioFailure`.

Adjacent upgrade validation matrix: `powershell -ExecutionPolicy Bypass -File .\scripts\test-installer-bootstrap-matrix.ps1 -ScenarioNames AdjacentUpgrade -PreviousMsiPath C:\path\to\AdagioMachineAgentSetup-previous.msi -MsiPath .\installer\bin\x64\Release\AdagioMachineAgentSetup.msi -FailOnScenarioFailure`.

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

Local-only extension telemetry:

- The extension writes `TELEMETRY:first_activation` the first time it activates.
- The extension writes `TELEMETRY:first_successful_command` the first time any Adagio tool completes successfully.
- These markers are written only to the local VS Code output channel and are not sent off-machine.

**Build:**

```bash
cd controller-extension
npm install
npm run compile
```

**Configuration** (`.vscode/settings.json` or VS Code UI):

| Setting | Default | Description |
|---|---|---|
| `adagioAgent.vmAgentUrl` | `https://127.0.0.1:5443/api/v1` | VM agent base URL |
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
| `message` | string | Alias of `error` for clients expecting a message field |
| `detail` | string? | Optional diagnostic detail |
| `correlationId` | string? | Correlation token that maps user-visible errors to backend request logs |
| `errorCode` | string? | Machine-readable failure category |
| `remediationHint` | string? | Action the caller/operator should take next |

Example:

```json
{
  "error": "Missing required header 'X-API-Key'.",
  "message": "Missing required header 'X-API-Key'.",
  "detail": null,
  "correlationId": "0HNK4PIRJOR0P",
  "errorCode": "UNAUTHORIZED",
  "remediationHint": "Set the configured API key in the request header and retry."
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
sudo apt-get install at-spi2-core libx11-6 xdotool

# Fedora / RHEL
sudo dnf install at-spi2-core libX11 xdotool
```

Applications must support AT-SPI2 accessibility (all GTK and Qt applications
do by default; Electron apps require `--force-renderer-accessibility`).

On Linux, `/send-keys` and `/press-hotkey` use `xdotool` and require an active
X11 window for the target process.

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
| Start type | Automatic (starts on boot; startup is verified during install) |
| Service account | `NT AUTHORITY\LocalService` |
| Listens on | `https://127.0.0.1:5443` |
| Uninstall | Stops & removes the service, deletes all installed files |
| Upgrade | Major-upgrade (replaces binaries in-place; preserves existing `appsettings.json`) |

**Install / Uninstall:**

```powershell
# Install
msiexec /i AdagioMachineAgentSetup.msi /quiet

# Uninstall
msiexec /x AdagioMachineAgentSetup.msi /quiet
```

Or use **Add / Remove Programs** for a GUI experience.

**Configuration during installation:**

On first install, MSI runs `bootstrap-agent.ps1` automatically (elevated) to:

- generate a self-signed certificate at `C:\ProgramData\AdagioMachineAgent\tls\agent.pfx`
- generate a random API key
- write startup-critical values into `%ProgramFiles%\AdagioMachineAgent\appsettings.json`
- write bootstrap handoff secrets to `%ProgramData%\AdagioMachineAgent\bootstrap-secrets.json`
  with restricted ACLs (`SYSTEM` and `Administrators` only)

MSI then attempts to start `AdagioMachineAgent`. If startup validation fails,
installation fails (error 1920) and rolls back.

When startup fails, inspect:

- MSI verbose log (`msiexec /i ... /l*v install.log`)
- `%ProgramData%\AdagioMachineAgent\bootstrap.log`
- `%ProgramData%\AdagioMachineAgent\bootstrap-failure.json`
- `%ProgramData%\AdagioMachineAgent\bootstrap-preflight.log`
- `%ProgramData%\AdagioMachineAgent\bootstrap-preflight-failure.json`
- `%ProgramData%\AdagioMachineAgent\startup-failure.json`

Failure JSON files include a `suggestedAction` field with a first remediation
step tailored to the detected error, and an `errorCode` field for fast support
triage.

Bootstrap secret handoff guidance:

- `%ProgramData%\AdagioMachineAgent\bootstrap-secrets.json` contains the generated
  API key and certificate password for initial operator handoff.
- The file is ACL-restricted to `SYSTEM` and local `Administrators`.
- After securely transferring secrets to the target operator secret store,
  delete the handoff file.

Installer validation automation:

- `scripts/test-installer-bootstrap-matrix.ps1` runs a guarded fresh silent MSI install validation on a clean elevated Windows machine.
- The same harness supports `AdjacentUpgrade` when a baseline MSI is supplied, and verifies that appsettings values and bootstrap handoff secrets are preserved across the upgrade.
- The script verifies service startup, bootstrap diagnostics, handoff ACLs, installed appsettings wiring, and authenticated health/diagnostics endpoint probes.
- AppVeyor runs fresh-install validation automatically after building the MSI and can run adjacent-upgrade validation when `ADAGIO_UPGRADE_BASELINE_MSI` is supplied in the build environment. Summary and log artifacts are published from `artifacts/installer-validation/`.

**Installer Error Code Reference:**

| Code | Source artifact | Meaning | First action |
|---|---|---|---|
| `AA1001` | `bootstrap-failure.json` | Permission/certificate-store access issue during bootstrap | Re-run installer as administrator and review local certificate policy restrictions |
| `AA1002` | `bootstrap-failure.json` | `appsettings.json` not found during bootstrap | Verify installation folder content and rerun install |
| `AA1003` | `bootstrap-failure.json` | Certificate creation failed in both LocalMachine and CurrentUser stores | Check certificate enrollment/service policy, then rerun |
| `AA1099` | `bootstrap-failure.json` | Unclassified bootstrap failure | Inspect `bootstrap.log` and rerun |
| `AA2001` | `bootstrap-preflight-failure.json` | Placeholder security values still present (for example `CHANGE_ME`) | Re-run installer to regenerate values or set real values manually |
| `AA2002` | `bootstrap-preflight-failure.json` | Configured HTTPS certificate file is missing | Correct certificate path or regenerate certificate |
| `AA2003` | `bootstrap-preflight-failure.json` | HTTPS certificate cannot be loaded (commonly wrong password) | Correct certificate password/file and rerun |
| `AA2004` | `bootstrap-preflight-failure.json` | API key required but empty | Set non-empty `SecurityOptions.ApiKey` and rerun |
| `AA2099` | `bootstrap-preflight-failure.json` | Unclassified preflight validation failure | Inspect `bootstrap-preflight.log` and rerun |

**Configuration after installation (manual updates):**

If you later change security settings in `%ProgramFiles%\AdagioMachineAgent\appsettings.json`, restart the service:

```powershell
Restart-Service AdagioMachineAgent
```

Startup-critical values:

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
