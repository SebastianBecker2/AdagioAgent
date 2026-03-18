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
4. Confirm machine-agent and extension versions.

## Information To Include

- Host OS and version.
- Agent version and extension version.
- Exact command/tool used when issue occurred.
- Error messages and timestamps.
- Sanitized diagnostics output and readiness payload.
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
