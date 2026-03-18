# Support

## Product Posture

AdagioAgent is currently positioned as a Windows-first, admin-managed tool for controlled environments.

See `docs/OPERATING_MODEL.md` for support boundaries and deployment assumptions.

## Before Requesting Support

1. Run the VS Code command `Adagio Agent: Run Startup Diagnostics`.
2. Open `Adagio Agent: Open Diagnostics Output` and capture relevant lines.
3. Collect endpoint outputs:
   - `/api/v1/ready`
   - `/api/v1/diagnostics/status`
   - `/api/v1/diagnostics/export-metadata`
4. Collect a support bundle:

```powershell
.\scripts\collect-support-bundle.ps1 -ApiKey '<api-key>'
```

If you exported extension diagnostics separately, include path metadata in the
bundle:

```powershell
.\scripts\collect-support-bundle.ps1 -ApiKey '<api-key>' -ExtensionOutputPath 'C:\path\to\adagio-output.log'
```

Manifest schema and artifact categories are documented in
`docs/SUPPORT_BUNDLE_SCHEMA.md`.

5. Confirm machine-agent and extension versions.

## Correlation ID guidance

- Capture `Correlation ID` values from extension error/warning messages.
- Include those IDs in support tickets and incident notes.
- Use the ID to match the user-visible failure with backend request logs and
   support-bundle artifacts.
- If multiple errors are observed, list each correlation ID with timestamp and
   command/tool context.

Retention and timeline guidance:

- Preserve correlation IDs and timestamps in incident notes for at least one
   full release cycle after incident closure.
- Build incident timelines using: user symptom time, correlation ID,
   backend request log, backend error log, remediation action time.
- Keep timeline entries ordered in UTC to avoid cross-machine timezone drift.

## Information To Include

- Host OS and version.
- Agent version and extension version.
- Exact command/tool used when issue occurred.
- Error messages and timestamps.
- Correlation IDs (if present).
- Sanitized diagnostics output and readiness payload.
- Support bundle folder path and manifest.
- Whether issue is deterministic or intermittent.

Do not include secrets, API keys, or certificate private keys.

## Issue Types

- Installation and service startup failures.
- Connection, TLS, and API-key authentication issues.
- Endpoint behavior regressions.
- Extension command failures and diagnostics UX issues.

## Response Model

Support is best-effort during active productization.

- Critical blockers: prioritized.
- Non-blocking defects and enhancement requests: handled by roadmap priority.

## Severity Matrix And Triage SLA Targets

| Severity | Definition | Initial Triage Target | Mitigation Target |
|---|---|---|---|
| Sev-1 | Service unavailable, broad pilot impact, no workaround | 1 business hour | Same business day |
| Sev-2 | Major degradation, limited workaround, pilot work blocked | 4 business hours | 2 business days |
| Sev-3 | Functional defect with workaround, non-critical flow impact | 1 business day | Planned in next iteration |
| Sev-4 | Documentation, UX polish, minor enhancements | 2 business days | Backlog-prioritized |

These are target service levels during active productization, not hard
guarantees.

## Operational Docs Ownership And Review Cadence

Operational docs owners (role-based):

- `Backend Owner`: `docs/OBSERVABILITY_FIELDS.md`, API-error and correlation behavior docs.
- `Support Owner`: `SUPPORT.md`, `docs/DIAGNOSTICS_TROUBLESHOOTING.md`, severity/SLA policies.
- `Release Owner`: `docs/RELEASING.md`, release-support quickstart and preflight procedures.
- `Pilot Operations Owner`: `docs/PILOT_RUNBOOK.md`, rollback/upgrade checklists.

Review cadence:

- Minimum monthly review of all operational docs.
- Mandatory review in any release where logging fields, error contracts,
  support-bundle schema, or incident workflows changed.
