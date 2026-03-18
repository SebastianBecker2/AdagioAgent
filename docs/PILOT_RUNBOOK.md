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

### Incident response flow

1. Detect
   - Readiness degraded, startup failure, command failure, or service outage.
2. Contain
   - Pause further upgrades/deployments in pilot scope.
   - Preserve current state and collect support bundle.
3. Diagnose
   - Analyze readiness issues, diagnostics status, export metadata, and event logs.
4. Mitigate
   - Apply temporary remediation or rollback using rollback checklist.
5. Recover
   - Re-run validation checklist and restore pilot traffic.
6. Learn
   - Document root cause and update checklists/docs/preflight checks.

### Evidence package per incident

- Timestamped support bundle output.
- Before/after readiness payloads.
- Relevant extension diagnostics output excerpts.
- Installer/service operation logs.
- Decision log (mitigation vs rollback).

### Exit criteria

Pilot can exit when all are true:

- Install and upgrade checklists pass on representative machines.
- Rollback is successful and documented.
- At least one real incident run has complete evidence and closure notes.
- Operational docs are sufficient for non-author maintainers.
