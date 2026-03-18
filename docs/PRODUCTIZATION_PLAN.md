## Productization Plan

Turn AdagioAgent from an engineer-operated tool into a supportable product by sequencing work in seven phases: define the supported operating model, automate secure deployment, harden backend operations, improve extension onboarding and admin UX, formalize the API contract, professionalize release and compliance, and run a controlled pilot. The recommended approach is Windows-first and admin-managed, with Linux parity, richer auth, and broader distribution treated as follow-on work after deployment, observability, and supportability are stable.

## Progress Tracker

- [x] Document supported operating model and support boundaries.
- [x] Decide bootstrap strategy for certificate and API key provisioning.
- [x] Add startup validation and readiness endpoint for secure install verification.
- [x] Add a first-run extension health check.
- [x] Implement certificate bootstrap helper path for first-service-start.
- [x] Implement API key provisioning and secure storage strategy for controlled environments.
- [x] Add readiness checks for certificate validity window and API key quality.
- [x] Add extension command to re-run startup diagnostics on demand.
- [x] Add readiness checks for UI automation backend capability.
- [x] Add diagnostics endpoint with summarized startup/runtime health data.
- [x] Add extension status indicator for readiness state.
- [x] Add structured logging output channel in the extension.
- [x] Add OpenAPI/Swagger discoverability for canonical `/api/v1` contract.
- [x] Add diagnostics export metadata endpoint for support bundle workflows.
- [x] Add extension command to open diagnostics output and current readiness status.
- [x] Add readiness/diagnostics troubleshooting workflow documentation.
- [x] Add CHANGELOG with initial release and roadmap-relevant entries.
- [x] Add SECURITY policy with reporting and vulnerability handling expectations.
- [x] Add SUPPORT policy with boundaries, required diagnostics, and response model.
- [x] Add CONTRIBUTING guide with branch, PR, and test expectations.
- [x] Add CI checks for changelog presence on versioned release tags.
- [x] Add CI validation for governance document presence.
- [x] Add release preflight script for backend/extension/installer version consistency.
- [x] Document release preflight workflow and tag gates in release checklist.
- [x] Add support bundle script for sanitized diagnostics payload and operational evidence collection.
- [x] Add rollback checklist for installer upgrades.
- [x] Add adjacent-version upgrade validation checklist.
- [x] Add pilot runbook with incident response flow.

### Phase 0: Product Definition And Support Boundaries

Lock the product shape before adding more surface area.

- Decide and document supported OSes, deployment topology, network assumptions, service-account model, certificate strategy, credential ownership, release cadence, and support window.
- Recommended baseline: Windows-first, single-tenant and admin-managed deployment, HTTPS required, `/api/v1` as the only documented API, Linux marked preview until parity is proven.
- This phase blocks later commitments in docs, support, security, and release policy.

### Phase 1: Secure Installation And First-Run Bootstrap

Replace manual post-install editing with a deterministic bootstrap path.

- Provision or validate the HTTPS certificate.
- Generate and store an API credential securely.
- Produce masked runtime configuration.
- Expose a first-run validation path.
- Preferred approach: first-service-start provisioning over complex WiX custom actions unless enterprise PKI integration is already available.

Deliverable outcome: a new installation becomes usable without hand-editing raw config files.

### Phase 2: Backend Operational Hardening

Add production-grade startup validation, readiness and liveness separation, graceful shutdown, request correlation, structured audit logging, and support diagnostics.

- Standardize exception handling so failures are safe and debuggable.
- This is the minimum operational bar for production use.

### Phase 3: Extension Onboarding, Diagnostics, And Admin UX

Run this in parallel with late Phase 2 once the bootstrap contract is stable.

- Add activation and onboarding flow.
- Add connection health checks.
- Add status indicator, settings validation, progress UI, and output-channel logging.
- Add compatibility warnings sourced from the health contract.

Goal: reduce support load and make the system operable by someone other than the author.

### Phase 4: API Discoverability And Contract Governance

After backend operational hardening is in place:

- Expose OpenAPI and Swagger.
- Formalize response and error models.
- Add readiness and diagnostic endpoints.
- Document deprecation and support policy.
- Keep `/api/v1` as the canonical contract even while legacy aliases remain for compatibility.

### Phase 5: Release Engineering, Trust, And Compliance

Largely parallel with Phases 3 and 4 after backend hardening.

- Add code signing for MSI and VSIX.
- Add SBOM generation.
- Add dependency scanning.
- Add release automation.
- Add changelog, security, and support policies.
- Add branch and release governance.

This is what turns internal build outputs into distributable enterprise-grade artifacts.

### Phase 6: Pilot Readiness And Support Operations

Run a controlled pilot with:

- an install checklist
- rollback procedure
- support-bundle workflow
- telemetry and log review
- upgrade validation between adjacent versions

Exit only when installation, upgrade, support, and incident response are repeatable.

## Execution Detail

### 1. Product definition and support posture

Document supported environments, who installs and manages the agent, where secrets live, how upgrades happen, and what "supported" means.

Decision to make now: whether Linux is preview or fully supported.

### 2. Bootstrap and installation

Implement certificate provisioning and validation, API key generation and storage, config templating, and install validation.

Add a guided validation step that confirms the service can start securely and answer health and readiness checks.

### 3. Backend startup validation

Extend startup logic in `machine-agent/Program.cs` and security handling in `machine-agent/Services/SecurityPolicy.cs` so invalid certs, keys, or platform prerequisites fail fast with actionable diagnostics.

### 4. Backend readiness and diagnostics

Add readiness and diagnostic endpoints in `machine-agent/Controllers/AutomationController.cs` or a dedicated diagnostics controller, extend contracts in `machine-agent/Models/ApiModels.cs`, and add support-bundle generation with masked secrets.

### 5. Backend observability and audit

Add correlation IDs, structured request logging, command audit events, and Windows-friendly sinks.

Make errors consistent and machine-readable rather than endpoint-specific.

### 6. Lifecycle safety

Harden `machine-agent/Services/ProcessService.cs` for graceful shutdown, cancellation semantics where practical, cleanup on service stop and restart, and explicit concurrency and timeout behavior.

### 7. Extension activation and setup

Update `controller-extension/package.json` for real activation behavior, add setup and health-check flow in `controller-extension/src/extension.ts`, and improve connection and client behavior in `controller-extension/src/agentClient.ts`.

### 8. Extension diagnostics UX

Replace the current minimal error-only experience in `controller-extension/src/commandSafety.ts` with output-channel logging, settings validation, status bar state, progress reporting, and compatibility warnings.

### 9. API discoverability

Add OpenAPI and Swagger to the backend and document the canonical `/api/v1` surface in `README.md`.

Keep legacy aliases operational but undocumented except as deprecated compatibility paths.

### 10. Release engineering and trust

Expand `appveyor.yml` and `docs/RELEASING.md` to include signing, SBOM, dependency scanning, version and tag consistency, and automated release publication.

Add root-level governance and trust docs: `LICENSE`, `SECURITY.md`, `CHANGELOG.md`, `SUPPORT.md`, and `CONTRIBUTING.md`.

### 11. Pilot and hardening loop

Run installs, upgrades, and rollbacks in representative environments, validate support-bundle collection, review logs and telemetry, and close the last operational gaps before broader distribution.

## Verification

1. Installation verification: a clean Windows machine can install the MSI, provision security material, start the service, and pass a guided health check without manual file editing.
2. Operational verification: startup fails fast on invalid security config, readiness is separate from health, logs are structured and correlated, and support bundles mask secrets.
3. UX verification: a fresh VSIX install can discover setup, validate settings, show connection state, and surface actionable remediation for broken HTTPS, auth, or versioning.
4. Contract verification: OpenAPI describes the supported `/api/v1` surface; integration tests cover health, readiness, diagnostics, compatibility metadata, and selected command flows.
5. Release verification: CI produces signed MSI and VSIX artifacts, SBOMs, dependency-scan results, changelog and release notes, and rejects version, tag, and signing mismatches.
6. Pilot verification: at least one install, one upgrade, one rollback, and one failure-support scenario are executed end-to-end.

## Decisions

- Included scope: deployment, security bootstrap, observability, admin UX, release engineering, compliance basics, and pilot readiness.
- Excluded from immediate scope: full multi-tenant auth, broad Linux production support, cloud-hosted control plane, and API v2 design or removal of legacy aliases.
- Recommended sequencing: keep API major at v1, professionalize deployment and operations first, and only then broaden platform and support surface.
- Recommended product posture: admin-managed tool for controlled environments first, not general consumer software.

## Recommended Immediate Next Slice

Start with Phase 0 and the first thin slice of Phase 1:

1. [x] Document the supported operating model and support boundaries.
2. [x] Decide the bootstrap strategy for certificate and API key provisioning.
3. [x] Add startup validation and a readiness endpoint for secure install verification.
4. [x] Add a first-run extension health check after the backend bootstrap contract is stable.

## Next Active Slice

The next implementation slice should focus on completing bootstrap mechanics and
operator-facing diagnostics:

1. [x] Implement certificate provisioning helper path for first-service-start.
2. [x] Implement API key provisioning and secure storage strategy.
3. [x] Add readiness checks for certificate validity window and API key quality.
4. [x] Add extension command to re-run startup diagnostics on demand.

## Next Active Slice (Current)

The next implementation slice should focus on diagnostics and readiness depth:

1. [x] Add readiness checks for UI automation backend capability (not only config).
2. [x] Add a diagnostics endpoint that returns summarized startup/runtime health data.
3. [x] Add extension-side status indicator for readiness state.
4. [x] Add structured logging output channel in the extension.

## Next Active Slice (Current)

The next implementation slice should focus on API discoverability and supportability:

1. [x] Add OpenAPI/Swagger for `/api/v1` and core operational endpoints.
2. [x] Add diagnostics endpoint for support bundle/export metadata (without sensitive values).
3. [x] Add extension command/UI action to open the Adagio output channel and show current status.
4. [x] Add documentation for readiness/diagnostics troubleshooting workflow.

## Next Active Slice (Current)

The next implementation slice should focus on release trust and governance basics:

1. [x] Add `CHANGELOG.md` with initial release and roadmap-relevant entries.
2. [x] Add `SECURITY.md` with reporting process and vulnerability handling expectations.
3. [x] Add `SUPPORT.md` with support boundaries, data to collect, and response expectations.
4. [x] Add `CONTRIBUTING.md` with branch/PR/test requirements aligned to current CI.

## Next Active Slice (Current)

The next implementation slice should focus on release automation trust checks:

1. [x] Add CI checks that fail if `CHANGELOG.md` is missing updates for versioned release commits.
2. [x] Add CI validation that release-governance docs (`SECURITY.md`, `SUPPORT.md`) are present.
3. [x] Add a release-preflight script to validate version consistency across backend, extension, and installer.
4. [x] Document the preflight script workflow in `docs/RELEASING.md`.

## Next Active Slice (Current)

The next implementation slice should focus on pilot-readiness operations:

1. [x] Add a support bundle command/script that collects sanitized diagnostics payloads and recent logs.
2. [x] Add a documented rollback checklist for installer upgrades.
3. [x] Add an upgrade-validation checklist for adjacent-version upgrades.
4. [x] Add a pilot runbook section in docs with incident response flow.

## Next Active Slice (Current)

The next implementation slice should focus on backend observability and lifecycle safety:

1. Add request correlation ID propagation and structured request logging fields.
2. Standardize API error payloads for unhandled exceptions and validation failures.
3. Add graceful shutdown verification and cleanup diagnostics in process lifecycle paths.
4. Add backend tests for correlation/error-shape/lifecycle behavior regressions.