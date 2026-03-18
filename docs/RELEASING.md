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

### 5 - Commit the version bump

```powershell
git add machine-agent/AdagioMachineAgent.csproj controller-extension/package.json
git commit -m "chore: release vNEW"
```

### 6 - Create a release tag

```powershell
git tag -a vNEW -m "Release vNEW"
git push origin main --tags
```

AppVeyor will automatically build the tagged commit and produce the MSI
installer artifact.

### 7 - Publish artifacts

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