# Changelog

All notable changes to this project are documented in this file.

The format is inspired by Keep a Changelog and this project uses SemVer.

## [Unreleased]

### Added

- OpenAPI/Swagger discoverability for machine-agent API.
- Diagnostics export metadata endpoint (`/diagnostics/export-metadata`).
- VS Code command to open diagnostics output and show current readiness summary.
- Troubleshooting workflow documentation for readiness and diagnostics.
- Productization operating model and bootstrap strategy documentation.

### Changed

- Productization roadmap is now tracked as implementation slices with explicit completion checkboxes.

## [0.1.0] - 2026-03-18

### Added

- Initial machine-agent service and controller-extension integration.
- Versioned API contract support (`/api/v1`) with compatibility aliases.
- Core process execution, artifact collection, and UI automation command set.
- Backend and extension test suites with integration coverage for key contracts.
