# Release Checklist

This document describes the steps required to produce and publish a new
AdagioAgent release. Follow every step in order.

---

## Artifact versions

| File | Property | Notes |
|---|---|---|
| `machine-agent/AdagioMachineAgent.csproj` | `<Version>` | SemVer (e.g. `0.2.0`) |
| `controller-extension/package.json` | `"version"` | SemVer (e.g. `0.2.0`) |
| `installer/Package.wxs` | auto-derived | Read from machine-agent csproj at build time; no manual edit needed |

The installer MSI version is automatically derived from the machine-agent
`<Version>` value (with `.0` appended to satisfy WiX's 4-part requirement).

---

## Steps

### 1 - Decide the new version

Determine the next version following [Semantic Versioning](https://semver.org/):

- **PATCH** (`0.1.x`): bug fixes only, fully backward compatible.
- **MINOR** (`0.x.0`): new features, backward compatible.
- **MAJOR** (`x.0.0`): breaking changes (new API version path `/api/vN/...`
  required; deprecation of previous version aliases).

### 2 - Bump versions

Update both artifact version fields to the new version:

```powershell
# 1. machine-agent/AdagioMachineAgent.csproj
#    Change: <Version>OLD</Version>  ->  <Version>NEW</Version>

# 2. controller-extension/package.json
#    Change: "version": "OLD"  ->  "version": "NEW"
```

No change is needed in the installer project - it reads the version from the
machine-agent csproj automatically.

### 3 - Update the compatibility matrix

If the extension's supported API major version changed, update the table in
[README.md](../README.md) under **Versioning and Compatibility -> Compatibility
matrix**.

### 4 - Run all tests

```powershell
# .NET backend tests (machine-agent + integration)
dotnet test AdagioAgent.sln -c Release

# TypeScript extension tests
Set-Location controller-extension
npm test -- --run
Set-Location ..
```

All tests must pass before proceeding.

Release smoke check requirement (manual):

- Trigger one intentional API error (for example, call `/api/v1/health` without
   API key when API key auth is enabled) and verify:
   - response contains `X-Correlation-ID`
   - error payload includes `correlationId`
   - extension warning/error text surfaces the same correlation ID

### 4.5 - Run release preflight checks

Run the release preflight script from repository root:

```powershell
.\scripts\release-preflight.ps1
```

The script validates:

- backend and extension version parity
- installer version derivation consistency
- required governance docs presence (`CHANGELOG.md`, `SECURITY.md`, `SUPPORT.md`)
- tag and changelog consistency when running on a tagged release build

### 4.6 - Verify support-bundle execution against release artifacts

Run a support-bundle dry verification using the release build context:

```powershell
.\scripts\collect-support-bundle.ps1 -Offline
$latest = Get-ChildItem .\artifacts\support-bundles -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
.\scripts\validate-support-bundle.ps1 -BundlePath $latest.FullName -ExpectOffline
```

This verifies that the supportability packaging workflow remains functional for
the release candidate and that manifest/required artifacts are emitted.

### 5 - Update changelog for the release

Add a dedicated section in `CHANGELOG.md` for `NEW` including date and notable
changes.

### 5.5 - Verify observability docs freshness

Before tagging, verify these documents reflect current field names and support
workflows:

- `docs/OBSERVABILITY_FIELDS.md`
- `SUPPORT.md` (severity matrix + SLA targets)
- `docs/PILOT_RUNBOOK.md` (correlation timeline/evidence guidance)

If backend log fields or error-contract fields changed, update docs in the same
release commit.

### 5.6 - Complete operations sign-off template

Before tagging, fill and store a completed copy of:

- `docs/OPERATIONS_SIGNOFF_TEMPLATE.md`

Ensure required evidence is attached or linked (support bundle, correlation
trace, rollback rehearsal, upgrade validation).

You can scaffold a dated sign-off record from the template:

```powershell
.\scripts\generate-signoff-record.ps1 -Version NEW
```

Store generated records under `docs/release-ops/signoffs/` and reference the
selected record path in `CHANGELOG.md` for tagged releases.

Generate and populate a release evidence index:

```powershell
.\scripts\generate-evidence-index.ps1 -Version NEW
```

Use repo-relative evidence paths under `docs/release-ops/evidence/` where
possible. Approved external URI formats are `https://`, `s3://`, `gs://`,
`az://`, and UNC paths (`\\server\share`).

For tagged releases, ensure sign-off records include concrete evidence file
references and validate them:

```powershell
.\scripts\check-signoff-evidence-references.ps1
.\scripts\check-signoff-evidence-index-reference.ps1
.\scripts\check-evidence-index-content.ps1
```

### 6 - Commit the version bump

```powershell
git add machine-agent/AdagioMachineAgent.csproj controller-extension/package.json
git commit -m "chore: release vNEW"
```

### 7 - Create a release tag

```powershell
git tag -a vNEW -m "Release vNEW"
git push origin main --tags
```

AppVeyor will automatically build the tagged commit and produce the MSI
installer artifact.

Tagged builds are gated by release preflight checks: the tag version must match
backend/extension versions and `CHANGELOG.md` must contain a corresponding
section header.

### 8 - Publish artifacts

Download the `AdagioMachineAgentSetup` artifact from AppVeyor and attach it
to a GitHub release for the tag created in step 6.

---

## API version changes (MAJOR releases only)

When introducing breaking REST API changes:

1. Add new endpoints under `/api/v2/...`.
2. Keep `/api/v1/...` (and its legacy aliases) alive for at least one minor
   release cycle unless a clean break is agreed.
3. Update `HealthResponse.ApiVersion` to `2` in `AutomationController.cs`.
4. Update `MinSupportedClientVersion` as appropriate.
5. Remove the deprecated `/api/v1/...` aliases once support is dropped
   (see **Phase 5.3** in [VERSIONING_PLAN.md](VERSIONING_PLAN.md)).