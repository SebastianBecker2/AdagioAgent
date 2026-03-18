## Release Ops Records

This folder stores operational sign-off records for release candidates and tagged
releases.

### Storage convention

- Sign-off records are stored under `docs/release-ops/signoffs/`.
- Evidence indexes and artifacts are stored under `docs/release-ops/evidence/`.
- Naming convention: `v<semver>-<yyyymmdd>.md`.
- Records are generated from `docs/OPERATIONS_SIGNOFF_TEMPLATE.md` using:

```powershell
.\scripts\generate-signoff-record.ps1 -Version <semver>
```

Evidence indexes can be scaffolded using:

```powershell
.\scripts\generate-evidence-index.ps1 -Version <semver>
```

### Cross-link example

In sign-off record `docs/release-ops/signoffs/v<semver>-<yyyymmdd>.md`:

```markdown
- Evidence index path: docs/release-ops/evidence/indexes/v<semver>-<yyyymmdd>-evidence.md
```

In evidence index `docs/release-ops/evidence/indexes/v<semver>-<yyyymmdd>-evidence.md`:

```markdown
- SignOffRecord: docs/release-ops/signoffs/v<semver>-<yyyymmdd>.md
```

### Release reference requirement

For tagged releases, `CHANGELOG.md` should include a reference to the matching
sign-off record file (enforced in CI tagged builds).

### Validator scripts and expected behavior

- `scripts/check-signoff-evidence-references.ps1`
	- Passes when required sign-off evidence labels are populated with concrete
		repo-relative evidence paths (or approved external URI formats).
	- Fails when labels are missing, values are placeholders, or repo-relative
		evidence paths are invalid/non-existent.
- `scripts/check-signoff-evidence-index-reference.ps1`
	- Passes when tagged-release sign-off record includes a concrete `Evidence
		index path` and the referenced index cross-links back to the same sign-off
		file.
	- Fails when `Evidence index path` is missing/placeholder/invalid, missing on
		disk, or cross-link target does not match.
- `scripts/check-evidence-index-content.ps1`
	- Passes when the evidence index contains required entries (`Support bundle`,
		`Correlation trace`, `Rollback rehearsal`, `Upgrade validation`) with
		concrete values and valid paths.
	- Fails when required entries are missing, placeholders are used, version
		scoping is incorrect, or referenced files are absent.

### Dry-run package walkthrough

Use dry-run generation to verify release-ops package wiring before pilot
handoff and before tagging a release.

Generate a local dry-run package:

```powershell
.\scripts\generate-release-ops-dry-run.ps1 -OutputRoot .\artifacts\release-ops-dry-run -Force
```

Validate the latest dry-run package under a root:

```powershell
.\scripts\validate-release-ops-dry-run.ps1 -OutputRoot .\artifacts\release-ops-dry-run
```

Validate a specific dry-run package path:

```powershell
.\scripts\validate-release-ops-dry-run.ps1 -PackagePath .\artifacts\release-ops-dry-run\v<semver>-<yyyymmdd>-dryrun
```

Dry-run script parameters and common usage patterns:

- `generate-release-ops-dry-run.ps1`
	- `-OutputRoot`: choose where dry-run package folders are generated.
	- `-Version`: optional semver override for fixture naming.
	- `-Force`: overwrite existing package directory for same version/date.
- `validate-release-ops-dry-run.ps1`
	- `-OutputRoot`: validates latest package under root when `-PackagePath` is omitted.
	- `-PackagePath`: validates one explicit dry-run package directory.
	- `-SummaryOutputPath`: writes machine-readable JSON validation summary
		(`success`, `error`, categorized `issues`) for CI diagnostics.

### Dry-run validator failure categories

`validate-release-ops-dry-run.ps1` reports categorized issues in summary output:

- `structure`: required folders/files missing under dry-run package root.
- `manifest`: invalid/missing manifest fields or malformed values.
- `signoff`: sign-off file reference missing or mismatched evidence-index field.
- `index`: evidence index missing labels or sign-off cross-link mismatch.
- `fixture`: fixture file paths referenced in manifest but missing on disk.

Use category counts to route fixes quickly: structure/fixture for generation
issues, manifest/index/signoff for content or cross-link problems.

### Dry-run diagnostics summary retention

- Recommended retention for dry-run validation summary JSON files is 14 days.
- CI prunes stale summary files before writing new diagnostics.
- For local development, run manual pruning when diagnostic summaries are no
	longer needed for incident triage.

### Diagnostics index and trend interpretation

Generate/update diagnostics index from retained summaries:

```powershell
.\scripts\update-release-ops-diagnostics-index.ps1 -DiagnosticsRoot .\artifacts\release-ops-dryrun-diagnostics -MaxEntries 20
```

Index outputs:

- `dryrun-diagnostics-index.json`: machine-readable recent diagnostics summary.
- `dryrun-diagnostics-index.md`: human-readable recent outcomes list.

Trend guidance across consecutive builds:

- Stable readiness: consecutive `SUCCESS` entries with low issue counts.
- Regression signal: repeated `FAILURE` entries with same category (for example,
	`fixture` or `index`) across adjacent builds.
- Flaky pipeline signal: alternating success/failure with no code changes in
	diagnostics scripts; investigate environment and cleanup timing.

### Pilot handoff diagnostics thresholds

Use the following criteria to decide whether diagnostics trends support proceeding
to pilot handoff or require a hold:

| Trend | Decision |
|-------|----------|
| Last 5 entries are all SUCCESS with 0 total issues | Pass — proceed to pilot handoff |
| Last 5 entries are all SUCCESS with ≤2 total issues | Pass with note — proceed and record minor issues in sign-off |
| Any FAILURE in the last 3 entries | Hold — resolve failure and confirm green trend before handoff |
| 2 or more FAILUREs in the last 5 entries | Hold — investigate common failure category and fix root cause |
| 3 or more consecutive FAILUREs with the same issue category | Escalate — assign release-ops owner and halt handoff |

To review recent counts from the diagnostics index:

```powershell
$index = Get-Content .\artifacts\release-ops-dryrun-diagnostics\dryrun-diagnostics-index.json | ConvertFrom-Json
$index | Select-Object totalEntries, successCount, failureCount
```

To verify the index is current after a dry-run run:

```powershell
.\scripts\check-release-ops-diagnostics-index-freshness.ps1 -DiagnosticsRoot .\artifacts\release-ops-dryrun-diagnostics
```

### CI status report schema

The CI status report (`release-ops-ci-status-report.json`) combines the
diagnostics index trend and freshness gate outcomes into a single structured
payload. Schema fields:

| Field | Type | Description |
|-------|------|-------------|
| `generatedAtUtc` | string | UTC timestamp of report generation |
| `diagnosticsRoot` | string | Absolute path of the diagnostics directory |
| `overallStatus` | string | `pass`, `pass-with-note`, `hold`, `escalate`, or `no-data` |
| `qualityGates.indexFresh.passed` | boolean | Whether the diagnostics index is current |
| `qualityGates.indexFresh.message` | string | Human-readable freshness gate outcome |
| `qualityGates.trendGate.passed` | boolean | Whether the trend gate cleared |
| `qualityGates.trendGate.level` | string | `pass`, `pass-with-note`, `hold`, `escalate`, or `no-data` |
| `qualityGates.trendGate.message` | string | Human-readable trend assessment |
| `summary.totalEntries` | int | Total entries tracked in diagnostics index |
| `summary.successCount` | int | Count of SUCCESS entries in index |
| `summary.failureCount` | int | Count of FAILURE entries in index |
| `summary.recentEntryCount` | int | Number of recent entries analyzed for trend |

Overall status interpretation:

- `pass`: All recent entries succeeded, zero issues — safe to proceed to pilot handoff.
- `pass-with-note`: All recent entries succeeded with minor issues — proceed and record in sign-off.
- `hold`: One or more failures or stale index — resolve before pilot handoff.
- `escalate`: 3+ consecutive failures with the same issue category — assign release-ops owner, halt handoff.
- `no-data`: No diagnostics entries available — run dry-run flow before handoff review.

Generate the report locally:

```powershell
.\scripts\generate-release-ops-ci-status-report.ps1 -DiagnosticsRoot .\artifacts\release-ops-dryrun-diagnostics
```

### Tag readiness summary schema

The tagged-build readiness summary (`release-ops-tag-readiness-summary.json`)
combines required tagged-release validator outcomes with diagnostics quality-gate
status into one release verdict.

| Field | Type | Description |
|-------|------|-------------|
| `generatedAtUtc` | string | UTC timestamp of summary generation |
| `tagName` | string | Tag under evaluation (`v<semver>`) |
| `readinessVerdict` | string | `ready`, `ready-with-note`, or `hold` |
| `readinessMessage` | string | Human-readable release readiness decision |
| `validatorSummary.total` | int | Number of required validator scripts executed |
| `validatorSummary.passed` | int | Number of validators that passed |
| `validatorSummary.failed` | int | Number of validators that failed |
| `validatorSummary.results[].name` | string | Validator identifier |
| `validatorSummary.results[].passed` | boolean | Per-validator pass/fail |
| `validatorSummary.results[].message` | string | Per-validator diagnostic message |
| `diagnosticsQualityGate.available` | boolean | Whether CI diagnostics report was available |
| `diagnosticsQualityGate.overallStatus` | string | CI diagnostics status (`pass`, `pass-with-note`, `hold`, `escalate`, `no-data`) |
| `diagnosticsQualityGate.trendLevel` | string | Trend gate level from CI report |
| `diagnosticsQualityGate.indexFreshPassed` | boolean | Diagnostics index freshness gate result |
| `diagnosticsQualityGate.trendGatePassed` | boolean | Trend gate pass/fail |

Validator mapping used by the readiness summary:

- `signoffEvidenceReferences` -> `scripts/check-signoff-evidence-references.ps1`
- `signoffEvidenceIndexReference` -> `scripts/check-signoff-evidence-index-reference.ps1`
- `evidenceIndexContent` -> `scripts/check-evidence-index-content.ps1`

Tagged-build usage:

```powershell
.\scripts\generate-release-ops-tag-readiness-summary.ps1 -TagName v<semver> -FailOnHold
```

### Tag readiness history index and trend interpretation

Archive and index recent tagged readiness summaries:

```powershell
.\scripts\update-release-ops-tag-readiness-history.ps1 -ReadinessRoot .\artifacts\release-ops-tag-readiness -ArchiveLatest -MaxEntries 20 -RetentionDays 180
```

History outputs:

- `release-ops-tag-readiness-history-index.json`: machine-readable trend index.
- `release-ops-tag-readiness-history-index.md`: human-readable recent verdict list.

Trend guidance across recent release tags:

- Promotion-ready trend: last 3 tagged summaries are `ready`.
- Caution trend: one `ready-with-note` in last 3 tags; ensure note closure in sign-off.
- Hold trend: any `hold` verdict in last 2 tags; block promotion until resolved.
- Escalation trend: 2 or more `hold` verdicts in last 5 tags; open release-ops incident and require owner sign-off.

### Promotion gate enforcement and override process

Evaluate readiness history against promotion thresholds:

```powershell
.\scripts\check-release-ops-promotion-gate.ps1 -ReadinessRoot .\artifacts\release-ops-tag-readiness -FailOnBlock
```

Promotion gate report outputs:

- `release-ops-promotion-gate-report.json`
- `release-ops-promotion-gate-report.md`

Promotion verdict semantics:

- `pass`: thresholds met (latest 3 verdicts are `ready`, no `hold` in latest 2).
- `director-approval-required`: latest 3 are acceptable but include `ready-with-note`; requires explicit release-ops director approval.
- `fail`: thresholds not met.

Exceptional promotion override flow (director approval required):

1. Record rationale and risk acceptance in the tagged sign-off record.
2. Obtain explicit release-ops director approval ID (ticket/change request).
3. Re-run gate with override reference:

```powershell
.\scripts\check-release-ops-promotion-gate.ps1 -ReadinessRoot .\artifacts\release-ops-tag-readiness -AllowDirectorOverride -DirectorApprovalReference <approval-id> -FailOnBlock
```

Escalation requirement:

- If the gate verdict is `fail`, or if `hold` appears in the latest 2 tagged summaries, release promotion is blocked and director override is not permitted.

### Promotion gate trend summary and audit guidance

Summarize promotion gate outcomes across recent tagged builds:

```powershell
.\scripts\update-release-ops-promotion-gate-trend.ps1 -ReadinessRoot .\artifacts\release-ops-tag-readiness -ArchiveLatest -MaxEntries 20 -RetentionDays 365
```

Trend outputs:

- `release-ops-promotion-gate-trend-index.json`
- `release-ops-promotion-gate-trend-index.md`

Audit signals to monitor over time:

- Override frequency: `directorOverrideUsedCount` should remain rare and justified by documented approval references.
- Escalation health: `blockedCount` and `verdictCounts.fail` should trend toward zero across recent tags.
- Governance pressure: repeated `directorApprovalRequired` outcomes suggest chronic release readiness debt and should trigger process review.

Recommended audit cadence:

1. Review trend index during each post-release retrospective.
2. If 2 or more overrides occur in the most recent 5 tagged builds, open a release-ops process improvement action.
3. If any `fail` verdict appears in the most recent 3 tagged builds, require escalation follow-up closure before next promotion.

### Closure package manifest

Generate a single closure package manifest for a tagged release that links
readiness, promotion-gate, sign-off, and evidence index artifacts:

```powershell
.\scripts\generate-release-ops-closure-package-manifest.ps1 -ReadinessRoot .\artifacts\release-ops-tag-readiness -TagName v<semver>
```

Manifest outputs:

- `release-ops-closure-package-manifest.json`
- `release-ops-closure-package-manifest.md`

Required linked artifacts in closure manifest:

- Sign-off record for tag version (`docs/release-ops/signoffs/v<semver>-*.md`)
- Evidence index referenced by sign-off record
- Tagged readiness summary JSON
- Tagged readiness history index JSON
- Promotion gate report JSON
- Promotion gate trend index JSON

Retention expectations:

- Keep closure package manifest with tagged release artifacts for audit traceability.
- Keep sign-off and evidence index in-repo for at least one full release cycle.
- Keep promotion/readiness trend summaries through the next release retrospective window.

### Closure manifest validation and remediation

Validate closure manifest completeness for tagged releases:

```powershell
.\scripts\check-release-ops-closure-package-manifest.ps1 -ReadinessRoot .\artifacts\release-ops-tag-readiness
```

Common closure manifest validation failures and fixes:

- Missing manifest file:
	regenerate with `generate-release-ops-closure-package-manifest.ps1` in tagged build context.
- Missing required linked artifacts:
	regenerate missing readiness/promotion outputs, then regenerate closure manifest.
- Stale `exists` flags:
	rerun closure manifest generation after final artifact set is complete.
- Tag/version mismatch:
	ensure `-TagName` matches the current release tag and rerun generator.

If validation fails in CI tagged builds, release handoff is blocked until the
closure manifest validates cleanly.

Expected output layout:

```text
artifacts/release-ops-dry-run/
	v<semver>-<yyyymmdd>-dryrun/
		manifest.json
		signoffs/
			v<semver>-<yyyymmdd>.md
		evidence/
			indexes/
				v<semver>-<yyyymmdd>-evidence.md
			support-bundles/
				v<semver>-dryrun-bundle.json
			correlation-traces/
				v<semver>-dryrun-trace.md
			rollback/
				v<semver>-dryrun-rollback.md
			upgrade-validation/
				v<semver>-dryrun-upgrade.md
```

Dry-run to tagged-release file mapping:

- `signoffs/v<semver>-<yyyymmdd>.md` (dry-run fixture) ->
	`docs/release-ops/signoffs/v<semver>-<yyyymmdd>.md`
- `evidence/indexes/v<semver>-<yyyymmdd>-evidence.md` (dry-run fixture) ->
	`docs/release-ops/evidence/indexes/v<semver>-<yyyymmdd>-evidence.md`
- `evidence/support-bundles/v<semver>-dryrun-bundle.json` ->
	`docs/release-ops/evidence/support-bundles/v<semver>-<artifact>.json`
- `evidence/correlation-traces/v<semver>-dryrun-trace.md` ->
	`docs/release-ops/evidence/correlation-traces/v<semver>-<trace>.md`
- `evidence/rollback/v<semver>-dryrun-rollback.md` ->
	`docs/release-ops/evidence/rollback/v<semver>-<rehearsal>.md`
- `evidence/upgrade-validation/v<semver>-dryrun-upgrade.md` ->
	`docs/release-ops/evidence/upgrade-validation/v<semver>-<validation>.md`

### Strict-mode troubleshooting tips

- In `Set-StrictMode -Version Latest`, single-object pipeline results can break
	`.Count` checks. Normalize file search results with array wrapping:
	`@(Get-ChildItem ...)`.
- Restore environment variables after tests (`APPVEYOR_REPO_TAG`,
	`APPVEYOR_REPO_TAG_NAME`) to avoid cross-test contamination.
- Avoid implicit null member access in validators; validate presence of regex
	matches before reading capture groups.

### Evidence retention and location convention

- Keep sign-off records and referenced evidence paths for at least one full
	release cycle after release closure.
- Store repository-tracked evidence under `docs/release-ops/evidence/` when
	possible.
- Use repository-relative paths in sign-off records for all evidence references.
- If evidence must remain external, include immutable location and access notes
	in the sign-off record.

### Retention and archive checklist

After the retention window expires:

1. Confirm no active pilot/support incident depends on the evidence set.
2. Move expired evidence files to approved archive storage.
3. Keep sign-off records and evidence index files in-repo, replacing expired
   evidence references with archive URI and archival date.
4. Record the archive action in release operations notes.
