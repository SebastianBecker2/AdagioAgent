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

4. Before pilot handoff and before tagging a release, run dry-run release-ops
   package generation to confirm sign-off/index/evidence wiring:

```powershell
.\scripts\generate-release-ops-dry-run.ps1 -OutputRoot .\artifacts\release-ops-dry-run -Force
```

5. Generate and review the CI status report before tagging a release:

```powershell
.\scripts\generate-release-ops-ci-status-report.ps1 -DiagnosticsRoot .\artifacts\release-ops-dryrun-diagnostics
Get-Content .\artifacts\release-ops-dryrun-diagnostics\release-ops-ci-status-report.md
```

   A `pass` or `pass-with-note` overall status is required before tagging. `hold`
   or `escalate` status requires resolution before proceeding.

6. Generate and review the tagged-release readiness summary before approving a
   release tag:

```powershell
.\scripts\generate-release-ops-tag-readiness-summary.ps1 -TagName v<semver>
Get-Content .\artifacts\release-ops-tag-readiness\release-ops-tag-readiness-summary.md
```

   Interpretation rule for tag approval:
   - `ready`: approve tag release flow.
   - `ready-with-note`: approve only with documented sign-off notes.
   - `hold`: do not approve tag; resolve failed validator(s) or diagnostics gate.

7. Review readiness verdict trend history before final release promotion:

```powershell
.\scripts\update-release-ops-tag-readiness-history.ps1 -ReadinessRoot .\artifacts\release-ops-tag-readiness -ArchiveLatest -MaxEntries 20 -RetentionDays 180
Get-Content .\artifacts\release-ops-tag-readiness\release-ops-tag-readiness-history-index.md
```

   Promotion trend rule:
   - last 3 tagged summaries should be `ready`, and
   - no `hold` verdict in the last 2 tagged summaries.

8. Enforce promotion gate and apply director-approval decision rules:

```powershell
.\scripts\check-release-ops-promotion-gate.ps1 -ReadinessRoot .\artifacts\release-ops-tag-readiness -FailOnBlock
```

   Director approval is explicitly required when gate verdict is
   `director-approval-required` (typically because `ready-with-note` appears in
   latest 3 summaries).

   Decision notes:
   - `pass`: continue promotion.
   - `director-approval-required`: require release-ops director approval ID and rerun with `-AllowDirectorOverride -DirectorApprovalReference <id>`.
   - `fail`: stop promotion; remediation required. Director override is not allowed for `fail`.

9. During post-release retrospective, review promotion gate trend health:

```powershell
.\scripts\update-release-ops-promotion-gate-trend.ps1 -ReadinessRoot .\artifacts\release-ops-tag-readiness -ArchiveLatest -MaxEntries 20 -RetentionDays 365
Get-Content .\artifacts\release-ops-tag-readiness\release-ops-promotion-gate-trend-index.md
```

   Retrospective audit checks:
   - confirm override usage remains rare and documented,
   - confirm fail/blocked outcomes are not recurring across recent tags.

10. Generate and attach the closure package manifest to release notes handoff:

```powershell
.\scripts\generate-release-ops-closure-package-manifest.ps1 -ReadinessRoot .\artifacts\release-ops-tag-readiness -TagName v<semver>
Get-Content .\artifacts\release-ops-tag-readiness\release-ops-closure-package-manifest.md
```

   Handoff requirement:
   - Attach the closure package manifest (JSON or Markdown) to release notes,
     or link it from the release notes body.
    - Treat manifest generation as a late-stage step; if readiness/promotion
       outputs are regenerated afterward, regenerate closure manifest before
       release handoff.

11. Verify closure manifest drift is clean before final promotion:

```powershell
.\scripts\check-release-ops-closure-package-drift.ps1 -ReadinessRoot .\artifacts\release-ops-tag-readiness
```

    Drift policy:
    - If drift is detected, rerun closure manifest generation and rerun drift
       check before promotion continues.

   Integrity attestation:

```powershell
.\scripts\generate-release-ops-closure-package-integrity-report.ps1 -ReadinessRoot .\artifacts\release-ops-tag-readiness -OutputDir .\artifacts\release-ops-tag-readiness
Get-Content .\artifacts\release-ops-tag-readiness\release-ops-closure-package-integrity-report.md
```

   - Confirm `IntegrityVerdict` is pass and `IssueCount` is 0 before handoff.
   - Attach or link the integrity report alongside closure manifest in release notes.

   Trend review:

```powershell
.\scripts\update-release-ops-closure-package-integrity-history.ps1 -ReadinessRoot .\artifacts\release-ops-tag-readiness -ArchiveLatest -MaxEntries 20 -RetentionDays 365
Get-Content .\artifacts\release-ops-tag-readiness\release-ops-closure-package-integrity-history-index.md
```

   - Review recent integrity verdict trend before final promotion sign-off.

   Integrity gate enforcement:

```powershell
.\scripts\check-release-ops-closure-package-integrity-gate.ps1 -ReadinessRoot .\artifacts\release-ops-tag-readiness -FailOnBlock
Get-Content .\artifacts\release-ops-tag-readiness\release-ops-closure-package-integrity-gate-report.md
```

   - Require gate verdict pass before final release promotion.

12. Review dry-run diagnostics index trends before pilot handoff approval:

```powershell
.\scripts\update-release-ops-diagnostics-index.ps1 -DiagnosticsRoot .\artifacts\release-ops-dryrun-diagnostics -MaxEntries 20
.\scripts\check-release-ops-diagnostics-index-freshness.ps1 -DiagnosticsRoot .\artifacts\release-ops-dryrun-diagnostics
```

   Diagnostics trend pass/hold decision table:

   | Trend | Decision | Action |
   |-------|----------|--------|
   | Last 5 entries: all SUCCESS, 0 issues | Pass | Proceed to pilot handoff |
   | Last 5 entries: all SUCCESS, ≤2 total issues | Pass with note | Proceed; record minor issues in sign-off |
   | Any FAILURE in last 3 entries | Hold | Resolve failure, re-run, confirm green trend |
   | 2+ FAILUREs in last 5 entries | Hold | Fix root cause, verify fix with dry-run |
   | 3+ consecutive FAILUREs with same category | Escalate | Assign release-ops owner, halt handoff |

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

### 8. Troubleshooting dry-run generation and cleanup

Safe output-root guidance:

- Local runs: prefer a disposable workspace path such as
   `./artifacts/release-ops-dry-run` and avoid shared synced folders.
- CI runs: prefer an ephemeral temp path such as
   `$env:TEMP\adagio-release-ops-dryrun-ci` and remove it after validation.
- Always use `-Force` for repeatable dry-run automation in preflight scripts.

- Dry-run package generation reports existing directory conflicts:
   rerun with `-Force` or choose a clean `-OutputRoot` path.
- Dry-run validation fails on missing required files:
   regenerate package and ensure `manifest.json`, sign-off, index, and all four
   evidence fixture files are present.
- CI/local cleanup errors on dry-run output root:
   close file handles/editors pointing into the dry-run folder and retry cleanup.

Escalation path for repeated failures before pilot handoff:

1. Stop pilot handoff until dry-run validation is green in both local run and CI.
2. Attach the latest dry-run summary JSON diagnostics to the release ops thread.
3. Assign follow-up owner from release ops and rerun pre-release verification
   after fixes.

Manual cleanup guidance for local diagnostics growth:

1. Prune old summary files with a retention window (example: 14 days):

```powershell
.\scripts\prune-release-ops-dryrun-diagnostics.ps1 -DiagnosticsRoot .\artifacts\release-ops-dryrun-diagnostics -RetentionDays 14
```

2. If disk pressure persists, archive needed summaries externally and clear the
   local diagnostics folder.

### 9. Troubleshooting closure manifest validation

Common failures and fixes:

- Closure manifest file missing in tagged build:
   run `generate-release-ops-closure-package-manifest.ps1` using the tagged
   build readiness root and rerun validation.
- Required linked artifact missing (readiness/promotion/sign-off/evidence):
   regenerate missing artifact(s), then regenerate closure manifest.
- Closure manifest validation shows stale `exists` flags:
   rerun closure manifest generation after all artifact-producing steps finish.
- Tag mismatch in manifest:
   regenerate closure manifest with correct `-TagName v<semver>`.

### 10. Troubleshooting closure manifest drift detection

Common failures and fixes:

- Drift check reports readiness/promotion output modified after manifest generation:
   rerun `generate-release-ops-closure-package-manifest.ps1` after all
   readiness and promotion scripts have finished.
- Drift check reports manifest path mismatch:
   ensure drift check and manifest generation use the same `-ReadinessRoot` and
   regenerate manifest in that context.
- Drift check reports `exists` mismatch:
   regenerate missing artifact(s) or remove stale references by regenerating the
   closure manifest.
- Late-stage rerun rule:
   if any readiness or promotion output is regenerated after closure manifest
   creation, always regenerate closure manifest and rerun both manifest
   validation and drift checks before release handoff.

### 11. Troubleshooting closure package integrity report

Common failures and fixes:

- Integrity report shows missing required linked artifact:
   regenerate missing readiness/promotion/sign-off/evidence artifact and rerun
   closure manifest generation before rerunning integrity report.
- Integrity report shows stale manifest exists flag:
   regenerate closure manifest from the same readiness root, then rerun drift
   and integrity checks.
- Integrity report is pass but hashes changed after late rerun:
   treat as post-manifest regeneration event and rerun manifest, drift, and
   integrity checks as one sequence before handoff.

### 12. Troubleshooting closure integrity trend history

Common failures and fixes:

- Integrity history index has no entries after tagged build:
   ensure integrity report generation ran first, then rerun integrity history
   update with `-ArchiveLatest`.
- Integrity history shows recurring fail verdicts:
   stop handoff and review linked artifact regeneration order; rerun manifest,
   drift, and integrity checks in sequence.
- Integrity history grows too large locally:
   reduce `-RetentionDays` for local runs or prune archived reports after
   exporting required audit artifacts.

### 13. Troubleshooting closure integrity gate

Common failures and fixes:

- Gate blocks due to recent fail verdict:
   inspect integrity history index entries and resolve artifact mismatches,
   then regenerate closure manifest and rerun integrity checks.
- Gate blocks due to insufficient recent pass entries:
   run additional tagged validation cycles and confirm latest integrity reports
   are pass before final promotion.
- Gate report missing:
   rerun integrity gate script from the same readiness root used for integrity
   report and integrity history generation.
