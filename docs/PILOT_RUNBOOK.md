## Pilot Runbook

This runbook defines how to operate a controlled pilot for AdagioAgent with repeatable diagnostics, rollback, and incident response.

### Pilot goals

- Validate installation, upgrade, and rollback repeatability.
- Validate readiness and diagnostics workflows for support handoff.
- Validate that incidents are diagnosable without direct developer intervention.

### Environment assumptions

- Windows-first deployment in admin-managed controlled environments.
- HTTPS and API key auth enabled.
- Canonical API contract consumed via `/api/v1`.

### Pilot phases

1. Day-0 install and bootstrap validation.
2. Day-1 operational smoke: startup diagnostics, endpoint checks, one process workflow.
3. Upgrade rehearsal: adjacent-version upgrade and validation.
4. Rollback rehearsal: controlled rollback to known-good build.

### Daily pilot checks

1. Run `Adagio Agent: Run Startup Diagnostics`.
2. Review diagnostics output channel for warnings/errors.
3. Query readiness/diagnostics endpoints.
4. Capture a support bundle for anomalies.

### Periodic support-bundle drill

At least once per pilot week, run a support-bundle drill even without an
incident:

1. Generate a bundle with `scripts/collect-support-bundle.ps1`.
2. Confirm `manifest.json` required artifacts are present.
3. Validate optional artifacts are present as expected for online/offline mode.
4. Record drill timestamp and operator in pilot notes.

### Correlation-ID operating rule

- Treat `Correlation ID` as the primary join key between extension-facing
   failures and backend logs.
- When a user reports an issue, request the correlation ID first, then locate
   matching request-completion and exception log entries.
- Add correlation IDs to pilot incident timelines and remediation notes.

### Incident response flow

1. Detect
   - Readiness degraded, startup failure, command failure, or service outage.
2. Contain
   - Pause further upgrades/deployments in pilot scope.
   - Preserve current state and collect support bundle.
3. Diagnose
   - Analyze readiness issues, diagnostics status, export metadata, and event logs.
   - Correlate extension-visible errors to backend logs using correlation IDs.
4. Mitigate
   - Apply temporary remediation or rollback using rollback checklist.
5. Recover
   - Re-run validation checklist and restore pilot traffic.
6. Learn
   - Document root cause and update checklists/docs/preflight checks.

### Sev-1/Sev-2 owner role mapping

- `Incident Commander`: coordinates triage, communications, and go/no-go decisions.
- `Backend Owner`: investigates machine-agent behavior, logs, and correlation traces.
- `Extension Owner`: investigates extension command UX, diagnostics output, and client-side errors.
- `Release Owner`: manages rollback/redeploy actions and artifact integrity checks.

Minimum assignment requirements:

- Sev-1: all four roles must be assigned.
- Sev-2: Incident Commander + one technical owner (Backend or Extension) + Release Owner.

### Evidence package per incident

- Timestamped support bundle output.
- Before/after readiness payloads.
- Relevant extension diagnostics output excerpts.
- Correlation IDs mapped to backend request/exception log entries.
- Installer/service operation logs.
- Decision log (mitigation vs rollback).

Retain correlation timeline artifacts for at least one full release cycle after
incident closure.

### Exit criteria

Pilot can exit when all are true:

- Install and upgrade checklists pass on representative machines.
- Rollback is successful and documented.
- At least one real incident run has complete evidence and closure notes.
- Operational docs are sufficient for non-author maintainers.
