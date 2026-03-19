# Installer Bootstrap And Startup Validation Plan

## Purpose

Define an installer experience that is easy for end users, avoids invalid first-run states, and provides clear failure diagnostics when startup configuration is broken.

## Problem Statement

The machine agent enforces fail-fast startup validation for security-critical configuration (API key and HTTPS certificate). If the MSI attempts to start the service before valid configuration exists, installation fails with MSI error 1920 and rolls back.

## Goals

1. Make first-time installation deterministic and user-friendly.
2. Provision startup-critical configuration during install.
3. Fail installation only when service startup validation fails after provisioning.
4. Surface actionable startup failure reasons to users.
5. Keep advanced configuration complexity out of v1.

## Non-Goals (v1)

1. Full editor for all appsettings keys in installer UI.
2. Rich advanced configuration wizard.
3. Broad redesign of service security defaults.

## Agreed Product Direction

### v1 baseline (implement first)

1. Use bootstrap provisioning during install:
- Invoke scripts/bootstrap-agent.ps1 from installer custom action.
- Generate certificate and API key.
- Write required values into installed appsettings.json.

2. Validate service startup during install:
- Attempt to start service after bootstrap provisioning.
- Wait for Running status within timeout.
- Fail install if service does not start.

3. Improve diagnostics:
- Ensure startup validation failures are logged in a location installer/support can read reliably.
- Installer error should include where to find startup diagnostics.

4. Keep installer UI simple:
- Recommended path is bootstrap defaults.
- No full advanced appsettings editor in v1.

### v1.5 optional (if needed)

Offer a manual mode for startup-critical settings only:
- API key
- HTTPS certificate path
- HTTPS certificate password
- Optional URL/RequireHttps controls if required

Manual mode should still enforce startup validation before install success.

### v2 (deferred)

Add an advanced configuration experience. Candidate approaches:
1. Structured advanced tab for selected additional settings.
2. Raw multiline appsettings JSON editor in installer UI.

If raw JSON editing is added, include validation safeguards before applying changes.

## Technical Design Notes

1. Installer sequencing:
- Replace unconditional early service start behavior with explicit provisioning + startup validation flow.
- Use deferred elevated custom actions for provisioning and startup checks.

2. Secret handling:
- Do not leak generated API keys or certificate password into MSI logs.
- Define a secure handoff mechanism for operator consumption.

3. Failure handling:
- Installation should fail only after provisioning has run and startup verification fails.
- Error message should reference startup diagnostics source (event log and/or startup failure artifact).

4. Upgrade behavior:
- Decide and document whether upgrades preserve existing appsettings values or re-bootstrap only on first install.

## Installer Error Code Reference

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

## Files In Scope (expected)

- installer/Package.wxs
- installer/AdagioMachineAgent.Setup.wixproj
- scripts/bootstrap-agent.ps1
- machine-agent/Program.cs
- machine-agent/Services/SecurityPolicy.cs
- README.md

## Verification Matrix

1. Fresh install on clean machine:
- Installer succeeds.
- Service reaches Running.
- Health endpoint responds.

2. Provisioning failure test:
- Force bootstrap failure.
- Installer fails with actionable message.

3. Startup validation failure test:
- Inject invalid post-bootstrap setting.
- Installer fails and startup reason is discoverable.

4. Silent install test:
- Non-interactive path works with bootstrap defaults.
- Logs are useful but do not expose secrets.

5. Upgrade test:
- Upgrade from previous version.
- Confirm intended config preservation behavior and startup outcome.

## Rollout Plan

1. Implement v1 bootstrap-first install flow.
2. Validate with local and CI installer runs.
3. Update user-facing install docs.
4. Collect user feedback from pilot usage.
5. Prioritize v1.5 manual mode or v2 advanced editing based on support signals.

### Rollout Progress

1. Local bootstrap validation automation: in place via `scripts/tests/BootstrapScripts.Tests.ps1`.
2. CI bootstrap validation automation: in place via AppVeyor Pester execution of bootstrap script tests.
3. Full installer end-to-end matrix (fresh/silent/upgrade) remains pending for dedicated MSI environment coverage.

## Decisions Status

1. Secure handoff for generated bootstrap secrets: resolved.
- Installer bootstrap writes `%ProgramData%\AdagioMachineAgent\bootstrap-secrets.json`.
- File ACL is restricted to `SYSTEM` and local `Administrators`.
- Operator should transfer secrets into approved secret storage, then delete handoff file.

2. Canonical startup diagnostics sink: resolved.
- Bootstrap diagnostics: `%ProgramData%\AdagioMachineAgent\bootstrap.log` and `bootstrap-failure.json`.
- Preflight diagnostics: `%ProgramData%\AdagioMachineAgent\bootstrap-preflight.log` and `bootstrap-preflight-failure.json`.
- Runtime startup diagnostics: `%ProgramData%\AdagioMachineAgent\startup-failure.json`.

3. Upgrade-time config ownership policy: resolved.
- MSI preserves existing `appsettings.json` across major upgrades (`NeverOverwrite="yes"` component behavior).
- Bootstrap provisioning and preflight custom actions run on first install only (`NOT Installed AND NOT WIX_UPGRADE_DETECTED`).

4. Advanced mode UX for v2: open.
- Decide whether v2 starts with structured fields or raw JSON editor.
