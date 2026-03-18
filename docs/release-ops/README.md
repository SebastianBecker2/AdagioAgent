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
