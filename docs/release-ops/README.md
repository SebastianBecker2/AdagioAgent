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
