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
- [x] Add correlation ID propagation and structured request logging fields.
- [x] Standardize API error envelopes for validation failures and unhandled exceptions.
- [x] Add graceful shutdown cleanup diagnostics in process lifecycle paths.
- [x] Add backend regression tests for correlation, standardized errors, and lifecycle cleanup.
- [x] Surface backend correlation IDs in extension error and diagnostics output paths.
- [x] Add extension-side correlation ID parsing/typing in API error handling.
- [x] Add extension tests for correlation-aware error messaging.
- [x] Update troubleshooting docs with correlation-ID-first support workflow.
- [x] Add concise API error contract section to README with field examples.
- [x] Add operator correlation-ID guidance in support and pilot runbook docs.
- [x] Add release smoke-check requirement for correlation-ID verification.
- [x] Add end-to-end upgrade checklist entry for backend-to-extension correlation propagation.
- [x] Add support-bundle option for extension output export path metadata.
- [x] Add support-bundle manifest schema notes for required vs optional artifacts.
- [x] Add periodic support-bundle drill step in pilot runbook.
- [x] Add CI doc-link lint/check for README and SUPPORT references.
- [x] Add support-bundle metadata/manifest validation script tests.
- [x] Add online/offline support-bundle output examples in docs.
- [x] Add release checklist step for support-bundle execution validation.
- [x] Add CI offline support-bundle generation and manifest verification step.
- [x] Add structured logging field reference documentation.
- [x] Add correlation-ID retention and incident timeline guidance.
- [x] Add support severity matrix and triage SLA targets.
- [x] Add release checklist verification for observability docs freshness.
- [x] Add observability-doc consistency check script for core field references.
- [x] Add CI execution step for observability-doc consistency check.
- [x] Add troubleshooting cross-links to observability field reference and support severity matrix.
- [x] Add release preflight observability-doc presence checks.
- [x] Add release-support quickstart checklist linking release, diagnostics, and support-bundle workflows.
- [x] Add explicit Sev-1/Sev-2 owner role mapping in pilot runbook.
- [x] Add operational docs ownership and review cadence policy.
- [x] Add CI check for README operational docs index completeness.
- [x] Add operations sign-off template for release gates and pilot readiness.
- [x] Add required evidence checklist in sign-off workflow (support-bundle, correlation trace, rollback rehearsal).
- [x] Add release checklist cross-link to operations sign-off template.
- [x] Add CI check coverage that sign-off template is linked in README operational docs index.
- [x] Add script to scaffold dated sign-off records into release-ops/signoffs.
- [x] Add README/SUPPORT guidance for sign-off record storage location.
- [x] Add CI tagged-build check for matching sign-off record reference.
- [x] Add release checklist command snippet for sign-off record generation.
- [x] Add sign-off template section for concrete evidence file references.
- [x] Add helper script to validate sign-off evidence references.
- [x] Add optional tagged-build CI check for sign-off evidence references.
- [x] Add docs guidance for evidence retention period and location conventions.

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

1. [x] Add request correlation ID propagation and structured request logging fields.
2. [x] Standardize API error payloads for unhandled exceptions and validation failures.
3. [x] Add graceful shutdown verification and cleanup diagnostics in process lifecycle paths.
4. [x] Add backend tests for correlation/error-shape/lifecycle behavior regressions.

## Next Active Slice (Current)

The next implementation slice should focus on extension-side observability alignment:

1. [x] Surface correlation IDs in extension error and diagnostics output when backend returns them.
2. [x] Add extension-side parsing/typing for correlation ID in error payloads.
3. [x] Add extension test coverage for correlation-aware error messaging and diagnostics logs.
4. [x] Update troubleshooting docs with a correlation-ID-first support workflow.

## Next Active Slice (Current)

The next implementation slice should focus on contract and observability documentation depth:

1. [x] Add a concise API error contract section to README (fields and examples).
2. [x] Add operator guidance for correlation-ID usage in SUPPORT.md and PILOT_RUNBOOK.md.
3. [x] Add a release checklist item requiring correlation-ID verification in smoke tests.
4. [x] Add one end-to-end test checklist entry covering correlation from backend to extension message.

## Next Active Slice (Current)

The next implementation slice should focus on supportability packaging automation:

1. [x] Add an option in support-bundle script to include extension output export path metadata.
2. [x] Add support-bundle manifest schema notes (required vs optional artifacts).
3. [x] Add runbook step for periodic support-bundle drill during pilot.
4. [x] Add CI lint/check ensuring docs referenced by README and SUPPORT links exist.

## Next Active Slice (Current)

The next implementation slice should focus on support bundle validation hardening:

1. [x] Add tests for support-bundle script metadata output and manifest shape validation.
2. [x] Add documentation examples for online and offline bundle collection outputs.
3. [x] Add release checklist step to verify support-bundle script execution against release artifacts.
4. [x] Add CI job step to run support-bundle script in offline mode and verify manifest creation.

## Next Active Slice (Current)

The next implementation slice should focus on production observability policy:

1. [x] Add structured logging field reference doc (core fields and semantics).
2. [x] Add correlation-ID retention and incident timeline guidance.
3. [x] Add support severity matrix and triage SLA targets in SUPPORT.md.
4. [x] Add release checklist verification for observability docs freshness.

## Next Active Slice (Current)

The next implementation slice should focus on support workflow consistency checks:

1. [x] Add a script to validate observability docs mention core fields (`CorrelationId`, `DurationMs`, error contract fields).
2. [x] Add CI step to run observability-doc consistency check.
3. [x] Add troubleshooting doc cross-links to observability field reference and support severity matrix.
4. [x] Add release preflight check for required observability docs presence.

## Next Active Slice (Current)

The next implementation slice should focus on release and support runbook consolidation:

1. [x] Add a single release-support quickstart checklist linking release, diagnostics, and support-bundle workflows.
2. [x] Add explicit owner/role mapping for Sev-1/Sev-2 incident handling in pilot docs.
3. [x] Add periodic doc-review cadence and ownership section for operational docs.
4. [x] Add CI check that operational docs index in README includes all required runbook/schema docs.

## Next Active Slice (Current)

The next implementation slice should focus on operational readiness sign-off workflow:

1. [x] Add an operations sign-off template doc for release gates and pilot readiness checks.
2. [x] Add required evidence checklist (support-bundle, correlation trace, rollback rehearsal).
3. [x] Add release checklist cross-link to sign-off template.
4. [x] Add CI check that sign-off template and required operational docs are linked in README.

## Next Active Slice (Current)

The next implementation slice should focus on operational template automation:

1. [x] Add a script to scaffold dated sign-off records from the template into a release-ops folder.
2. [x] Add README/SUPPORT guidance for where completed sign-off records are stored.
3. [x] Add CI check that release tags include a matching sign-off record reference.
4. [x] Add release checklist command snippet for generating sign-off records.

## Next Active Slice (Current)

The next implementation slice should focus on release-signoff evidence traceability:

1. [x] Add sign-off template section for linking concrete artifact paths (bundle, logs, checklist outputs).
2. [x] Add helper script to verify a sign-off record references required evidence files.
3. [x] Add CI optional check to validate evidence references for tagged-release sign-off records.
4. [x] Add docs guidance for evidence retention period and repository location conventions.

## Next Active Slice (Current)

The next implementation slice should focus on evidence repository conventions:

1. [x] Add `docs/release-ops/evidence/` structure guidance with category folders.
2. [x] Add script to scaffold evidence index files per release sign-off record.
3. [x] Add CI check ensuring sign-off records reference either repo-relative evidence paths or approved external URI formats.
4. [x] Add release quickstart section for evidence packaging handoff.

## Next Active Slice (Current)

The next implementation slice should focus on sign-off/evidence automation hardening:

1. [x] Add script check ensuring each tagged-release sign-off includes a matching evidence index reference.
2. [x] Add CI tagged-build step to validate evidence index presence for the release version.
3. [x] Add docs examples for cross-linking sign-off records and evidence index files.
4. [x] Add retention/archive checklist section for evidence pruning after retention window.

## Next Active Slice (Current)

The next implementation slice should focus on evidence integrity and drift detection:

1. Add script check that evidence index files contain all required categories and non-placeholder paths for tagged releases.
2. Add CI tagged-build step to run evidence index content validation.
3. Add docs guidance for evidence index update cadence when support bundles are regenerated.
4. Add quickstart troubleshooting notes for common evidence index/sign-off validation failures.