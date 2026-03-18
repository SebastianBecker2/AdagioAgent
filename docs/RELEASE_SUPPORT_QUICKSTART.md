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

### 5. Evidence packaging handoff

1. Generate or update the per-release evidence index:

```powershell
.\scripts\generate-evidence-index.ps1 -Version <semver>
```

2. Populate concrete evidence files in the index and ensure each sign-off evidence
   path is either:
   - repo-relative under `docs/release-ops/evidence/`, or
   - an approved external URI format (`https://`, `s3://`, `gs://`, `az://`, `\\server\share`).
3. Ensure sign-off record cross-links the index path and the evidence index
   references the sign-off record path.

4. On tagged release validation, run:

```powershell
.\scripts\check-signoff-evidence-references.ps1
.\scripts\check-signoff-evidence-index-reference.ps1
```

### 6. Retention and archive handoff

1. At the end of the retention window, confirm no active incident depends on
   the evidence set.
2. Archive expired raw evidence artifacts to approved immutable storage.
3. Keep the sign-off record and evidence index in-repo and update evidence paths
   to archive URIs with archive date notes.

### 7. Troubleshooting evidence/sign-off validation

Common failures and fixes:

- Missing `Evidence index path` in sign-off record:
   add a repo-relative path under `docs/release-ops/evidence/indexes/`.
- Evidence index fails cross-link check:
   ensure `- SignOffRecord:` points to the exact sign-off file path.
- Evidence index content check fails required entries:
   populate `Support bundle`, `Correlation trace`, `Rollback rehearsal`, and
   `Upgrade validation` with concrete non-placeholder values.
- Repo-relative path policy errors:
   use evidence paths under `docs/release-ops/evidence/...` or approved external
   URI formats only.
- Strict-mode script failures in local test runs:
   use array-wrapped file enumeration (`@(Get-ChildItem ...)`) and ensure tag
   env vars are reset between test cases.
