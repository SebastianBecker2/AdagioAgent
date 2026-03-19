# Changelog

All notable changes to this project are documented in this file.

The format is inspired by Keep a Changelog and this project uses SemVer.

## [Unreleased]

## [0.2.0] - 2026-03-19

### Added

- OpenAPI/Swagger discoverability for machine-agent API.
- Diagnostics export metadata endpoint (`/diagnostics/export-metadata`).
- VS Code command to open diagnostics output and show current readiness summary.
- Troubleshooting workflow documentation for readiness and diagnostics.
- Productization operating model and bootstrap strategy documentation.
- Correlation-aware error diagnostics across backend and extension, including correlation IDs in standardized API error payloads.
- Pilot support-bundle collection/validation tooling and support-bundle schema/checklist documentation.
- Release preflight automation and CI trust gates for versioning/governance/release readiness.
- Tagged-release sign-off evidence validation, evidence-index linkage checks, and evidence content integrity checks.
- Release-ops dry-run package automation, validation diagnostics capture, diagnostics indexing/freshness checks, and CI status reporting.
- Release readiness summary, promotion-gate enforcement/trending, and closure package governance (manifest, drift, integrity, integrity gate).
- MSI bootstrap preflight validation step before service startup.
- Bootstrap diagnostics artifacts for install troubleshooting:
	- `%ProgramData%\\AdagioMachineAgent\\bootstrap.log`
	- `%ProgramData%\\AdagioMachineAgent\\bootstrap-failure.json`
	- `%ProgramData%\\AdagioMachineAgent\\bootstrap-preflight.log`
	- `%ProgramData%\\AdagioMachineAgent\\bootstrap-preflight-failure.json`
	- `%ProgramData%\\AdagioMachineAgent\\startup-failure.json`
- Installer diagnostics `errorCode` and `suggestedAction` metadata for fast support triage.
- Installer troubleshooting error-code reference in README and installer bootstrap plan docs.

### Changed

- Productization roadmap is now tracked as implementation slices with explicit completion checkboxes.
- Support and observability documentation is now cross-linked with runbooks/checklists and validated in CI.
- Installer now runs bootstrap provisioning on first install before service start, and keeps fail-fast install behavior if startup/preflight validation fails.
- Installer upgrades now preserve existing `appsettings.json` values.
- Bootstrap script now supports certificate-store fallback (`LocalMachine` -> `CurrentUser`) and suppresses secret output in installer context.
- Bootstrap RNG generation now supports PowerShell 5.1 environments.

### Fixed

- Fixed duplicate WiX `AgentVersion` define behavior that caused `WIX0288` build failures.
- Fixed MSI custom-action PowerShell parsing bug that caused bootstrap execution to fail with Error 1722.

## [0.1.0] - 2026-03-18

### Added

- Initial machine-agent service and controller-extension integration.
- Versioned API contract support (`/api/v1`) with compatibility aliases.
- Core process execution, artifact collection, and UI automation command set.
- Backend and extension test suites with integration coverage for key contracts.
