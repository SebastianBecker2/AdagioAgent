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
