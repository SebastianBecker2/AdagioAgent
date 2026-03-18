## Adjacent-Version Upgrade Validation Checklist

Run this checklist for each adjacent-version upgrade path during pilot readiness.

Example: `0.1.0 -> 0.2.0`

### 1. Baseline capture on source version

1. Verify current version values:
   - machine-agent csproj `<Version>`
   - extension `package.json` `version`
2. Run startup diagnostics and record readiness state.
3. Collect a support bundle snapshot.

### 2. Execute upgrade

1. Install target MSI build.
2. Verify service registration and startup.
3. Confirm configured endpoint URL and API key settings remain valid.

### 3. Functional checks after upgrade

1. `GET /api/v1/health` returns expected version metadata.
2. `GET /api/v1/ready` does not introduce new blocking issues.
3. `GET /api/v1/diagnostics/status` and `/diagnostics/export-metadata` return valid payloads.
4. Extension command checks:
   - `Adagio Agent: Run Startup Diagnostics`
   - `Adagio Agent: Open Diagnostics Output`
5. Run at least one process execution and one diagnostics/artifacts workflow.

### 4. Compatibility checks

1. Validate legacy alias routes still behave as expected.
2. Confirm no extension regression for command registrations.
3. Confirm installer version mapping still follows `<semver>.0`.

### 5. Post-upgrade evidence

1. Collect a post-upgrade support bundle snapshot.
2. Record pass/fail for each checklist item.
3. Log any degradations and required remediation work.
