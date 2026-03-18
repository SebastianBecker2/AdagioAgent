## Diagnostics And Readiness Troubleshooting

Use this workflow when onboarding a new machine, investigating startup failures,
or collecting support context without exposing sensitive values.

### 1. Run extension startup diagnostics

- Command: `Adagio Agent: Run Startup Diagnostics`
- VS Code command id: `adagioAgent.runStartupDiagnostics`
- Outcome:
  - `ready`: startup checks passed.
  - `degraded`: configuration/runtime issues were detected.
  - `offline`: agent endpoint was unreachable or returned an unexpected failure.

If diagnostics are degraded or offline, continue with the steps below.

### 2. Open diagnostics output channel

- Command: `Adagio Agent: Open Diagnostics Output`
- VS Code command id: `adagioAgent.openDiagnosticsOutput`
- The output channel contains timestamped structured lines with startup and
  diagnostics context.

### 3. Validate health and readiness endpoints

Run on the machine where the extension can reach the agent:

```powershell
Invoke-RestMethod -Uri https://127.0.0.1:5443/api/v1/health -Headers @{ 'X-API-Key' = '<api-key>' }
Invoke-RestMethod -Uri https://127.0.0.1:5443/api/v1/ready -Headers @{ 'X-API-Key' = '<api-key>' }
Invoke-RestMethod -Uri https://127.0.0.1:5443/api/v1/diagnostics/status -Headers @{ 'X-API-Key' = '<api-key>' }
Invoke-RestMethod -Uri https://127.0.0.1:5443/api/v1/diagnostics/export-metadata -Headers @{ 'X-API-Key' = '<api-key>' }
```

Interpretation guidance:

- `/health` should return `healthy` when the service is up.
- `/ready` and `/diagnostics/status` provide actionable readiness issues.
- `/diagnostics/export-metadata` is safe to share in tickets because it only
  includes non-sensitive counts and flags.

### 4. Review Swagger/OpenAPI contract

- UI: `https://127.0.0.1:5443/swagger`
- JSON: `https://127.0.0.1:5443/swagger/v1/swagger.json`

Use this to verify expected endpoint names and payload models for `/api/v1`.

### 5. Common issue checklist

- API key missing or wrong header name (`X-API-Key` by default).
- HTTPS certificate path/password invalid.
- Linux UI automation missing `DISPLAY`/`WAYLAND_DISPLAY` or `DBUS_SESSION_BUS_ADDRESS`.
- Process command path outside configured allow-list.
- Service not running or listening on configured URL.

### 6. Minimal support bundle contents

Collect and attach:

- Extension output channel export.
- `/ready` response JSON.
- `/diagnostics/status` response JSON.
- `/diagnostics/export-metadata` response JSON.
- Installer/service startup logs.

Do not include raw API keys, certificate private keys, or unredacted secrets.
