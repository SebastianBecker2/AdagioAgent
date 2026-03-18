## Release Ops Records

This folder stores operational sign-off records for release candidates and tagged
releases.

### Storage convention

- Sign-off records are stored under `docs/release-ops/signoffs/`.
- Naming convention: `v<semver>-<yyyymmdd>.md`.
- Records are generated from `docs/OPERATIONS_SIGNOFF_TEMPLATE.md` using:

```powershell
.\scripts\generate-signoff-record.ps1 -Version <semver>
```

### Release reference requirement

For tagged releases, `CHANGELOG.md` should include a reference to the matching
sign-off record file (enforced in CI tagged builds).
