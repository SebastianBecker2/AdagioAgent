## Release And Support Quickstart Checklist

Use this checklist as a single handoff path before releasing and during pilot
support operations.

### 1. Pre-release verification

1. Run all tests:

```powershell
dotnet test AdagioAgent.sln -c Release
Set-Location controller-extension
npm test -- --run
Set-Location ..
```

2. Run release preflight:

```powershell
.\scripts\release-preflight.ps1 -Ci
```

3. Verify support-bundle workflow:

```powershell
.\scripts\collect-support-bundle.ps1 -Offline
$latest = Get-ChildItem .\artifacts\support-bundles -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
.\scripts\validate-support-bundle.ps1 -BundlePath $latest.FullName -ExpectOffline
```

### 2. Observability and docs verification

1. Verify README and support links:

```powershell
.\scripts\check-doc-links.ps1
.\scripts\check-operational-docs-index.ps1
.\scripts\check-observability-docs.ps1
```

2. Confirm operational docs are current:
   - `docs/OBSERVABILITY_FIELDS.md`
   - `docs/DIAGNOSTICS_TROUBLESHOOTING.md`
   - `docs/SUPPORT_BUNDLE_SCHEMA.md`
   - `docs/PILOT_RUNBOOK.md`
   - `docs/RELEASING.md`

### 3. Incident-ready support posture

1. Confirm support severity and SLA expectations in `SUPPORT.md`.
2. Confirm Sev-1/Sev-2 owner roles in `docs/PILOT_RUNBOOK.md`.
3. Ensure correlation-ID workflow is documented and understood by operators.

### 4. During pilot incidents

1. Capture correlation ID from extension messages.
2. Collect support bundle and include extension output metadata if available.
3. Build incident timeline in UTC and map correlation IDs to backend logs.
4. Apply mitigation or rollback and log closure notes.
