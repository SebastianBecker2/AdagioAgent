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
