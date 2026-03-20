## Plan: Guided Installer Wizard v1

Build a Burn bootstrapper application as the primary installer experience, keep MSI and bootstrap script as the execution backbone, and deliver full v1 configuration coverage (certificate strategy, API key workflow, URL/hosts, HTTPS/API key toggles, and path allowlists) while preserving unattended/silent install parity. The installer wizard should collect inputs, validate them, hand them to MSI/bootstrap deterministically, and finish with service started using final settings.

**Steps**
1. Phase 1: Product and Contract Definition
2. Define the wizard input contract as a versioned configuration payload with explicit mapping to machine-agent settings and bootstrap parameters. Include certificate source mode (provided cert vs generated CA-signed cert), API key mode (generated vs user-provided), endpoint binding inputs (URLs, allowed hosts), security toggles, and path allowlists.
3. Define unattended equivalence for every interactive field (bundle command-line switches and/or response file) so silent installs remain first-class. Mark this as blocking for all implementation phases.
4. Define compatibility policy for upgrades and reruns: preserve existing settings by default on upgrade, expose explicit replace/reset actions, and document precedence order (wizard input, response file, existing appsettings, bootstrap defaults).
5. Phase 2: Burn Bootstrapper Foundation
6. Create a WiX Burn bundle and Bootstrapper Application (BA) project that orchestrates prerequisite checks, collects wizard inputs, and invokes MSI with serialized install properties. Depends on Step 2.
7. Add preflight discovery in BA (active network adapters, IPv4 addresses, hostname, existing config/cert presence, service status) and pre-populate wizard controls from discovered values. Parallel with Step 8 after BA skeleton is in place.
8. Implement wizard screens and validation flow:
9. Certificate screen: choose provided certificate vs generated certificate authority flow.
10. Certificate export screen: explicit export location actions and copy-path affordances.
11. Security screen: API key display with hidden-by-default reveal and clipboard copy action, RequireHttps, RequireApiKey.
12. Network screen: URL builder and AllowedHosts multi-select populated from active interfaces.
13. Paths screen: AllowedExecutablePaths, AllowedWritablePaths, AllowedReadablePaths editors with validation.
14. Summary and confirm screen: final effective config preview before install.
15. Phase 3: MSI and Bootstrap Integration
16. Extend MSI custom action plumbing to accept BA-provided properties and pass normalized values into scripts/bootstrap-agent.ps1 and appsettings update flow. Depends on Steps 2 and 6.
17. Extend scripts/bootstrap-agent.ps1 to support explicit user-provided cert path/password mode, generated CA-signed mode, deterministic API key modes, and explicit output/export targets for CA PEM/PFX and server cert artifacts.
18. Ensure bootstrap-secrets payload remains the single handoff artifact for post-install client setup and now includes all wizard-relevant output paths. Parallel with Step 19.
19. Ensure service startup ordering guarantees final config is active before service start and avoids post-install manual restarts.
20. Phase 4: Backend Validation Hardening
21. Implement runtime enforcement for AllowedReadablePaths and AllowedWritablePaths in machine-agent endpoints, not only readiness warnings. Depends on Step 14 because wizard exposes these as first-class controls.
22. Expand startup and readiness validation to report actionable issues for URL/host mismatches, cert mode mismatches, and invalid path entries.
23. Add configuration schema versioning in machine-agent startup to safely parse evolving installer payloads and support upgrade compatibility.
24. Phase 5: UX Reliability and Diagnostics
25. Add structured installer diagnostics artifacts for each stage (BA preflight, MSI invoke, bootstrap execution, service start) with correlation IDs.
26. Add BA final screen with copyable connection details (vm URL, API key source, CA PEM path) and one-click export/open actions.
27. Add rollback guidance and recovery actions in BA when any stage fails (retry bootstrap, reopen config, export diagnostics bundle).
28. Phase 6: Validation, Release, and Adoption
29. Create full test matrix for interactive and unattended paths:
30. Fresh install with generated CA mode.
31. Fresh install with provided certificate mode.
32. Upgrade install preserving existing config.
33. Upgrade install with explicit replace.
34. Silent install parity for all critical settings.
35. Path enforcement regression tests for executable/read/write policies.
36. Validate extension startup diagnostics with strict TLS (no insecure mode) using exported CA PEM across at least two distinct client machines.
37. Update operator and user docs, quickstart, and release notes; include migration guidance from server-leaf PEM usage to CA PEM usage where relevant.
38. Gate release with pilot checklist and rollback checklist completion before GA.

**Relevant files**
- [installer/AdagioMachineAgent.Setup.wixproj](installer/AdagioMachineAgent.Setup.wixproj) — Add Burn bundle integration, build orchestration, artifact packaging.
- [installer/Package.wxs](installer/Package.wxs) — MSI property plumbing, custom action sequencing, service start ordering.
- [scripts/bootstrap-agent.ps1](scripts/bootstrap-agent.ps1) — Certificate mode handling, API key mode handling, export artifacts, secret handoff payload.
- [scripts/check-bootstrap-preflight.ps1](scripts/check-bootstrap-preflight.ps1) — Extend validation for wizard-provided values and failure diagnostics.
- [scripts/tests/BootstrapScripts.Tests.ps1](scripts/tests/BootstrapScripts.Tests.ps1) — Add test coverage for both cert modes, API key modes, and artifact outputs.
- [machine-agent/Program.cs](machine-agent/Program.cs) — Startup validation integration and configuration binding compatibility.
- [machine-agent/Services/SecurityPolicy.cs](machine-agent/Services/SecurityPolicy.cs) — Harden validation and readiness issue reporting for wizard-managed fields.
- [machine-agent/Services/PathPolicy.cs](machine-agent/Services/PathPolicy.cs) — Path policy enforcement semantics.
- [machine-agent/Services/ProcessService.cs](machine-agent/Services/ProcessService.cs) — Existing executable path enforcement reference.
- [machine-agent/Controllers/AutomationController.cs](machine-agent/Controllers/AutomationController.cs) — Endpoint-level read/write path enforcement and diagnostics responses.
- [machine-agent-tests/SecurityPolicyTests.cs](machine-agent-tests/SecurityPolicyTests.cs) — Extend policy test coverage for new validation behavior.
- [machine-agent-tests/ProcessServiceTests.cs](machine-agent-tests/ProcessServiceTests.cs) — Add path enforcement regression tests.
- [machine-agent-tests/VersioningIntegrationTests.cs](machine-agent-tests/VersioningIntegrationTests.cs) — Validate config contract compatibility over upgrades.
- [docs/QUICKSTART.md](docs/QUICKSTART.md) — Update guided install and unattended equivalence instructions.
- [docs/BOOTSTRAP_STRATEGY.md](docs/BOOTSTRAP_STRATEGY.md) — Update bootstrap decision model for guided flow.
- [docs/plans/INSTALLER_BOOTSTRAP_PLAN.md](docs/plans/INSTALLER_BOOTSTRAP_PLAN.md) — Link and reconcile scope boundaries with new wizard plan.
- [docs/plans/README.md](docs/plans/README.md) — Add plan index entry and status tracking.
- [docs/UPGRADE_VALIDATION_CHECKLIST.md](docs/UPGRADE_VALIDATION_CHECKLIST.md) — Add wizard upgrade scenarios.
- [docs/ROLLBACK_CHECKLIST.md](docs/ROLLBACK_CHECKLIST.md) — Add rollback actions for guided install failures.

**Verification**
1. Run extension tests in [controller-extension](controller-extension) and ensure startup diagnostics strict-TLS scenarios pass with CA PEM on clean machines.
2. Run bootstrap Pester suite in [scripts/tests/BootstrapScripts.Tests.ps1](scripts/tests/BootstrapScripts.Tests.ps1) with new cert/API key mode coverage.
3. Run machine-agent .NET test suite in [machine-agent-tests](machine-agent-tests) including new path enforcement tests.
4. Execute MSI + Burn interactive installs on clean Windows VMs and verify no manual json edits are required for baseline success.
5. Execute silent install variants with response-file or CLI property mapping and compare effective runtime config against interactive installs.
6. Validate service starts successfully with final wizard-selected settings and no post-install restart required.
7. Validate exported artifacts and clipboard-copy workflows for API key and CA PEM path on final wizard screen.
8. Complete upgrade and rollback checklist runs using [docs/UPGRADE_VALIDATION_CHECKLIST.md](docs/UPGRADE_VALIDATION_CHECKLIST.md) and [docs/ROLLBACK_CHECKLIST.md](docs/ROLLBACK_CHECKLIST.md).

**Decisions**
- Primary approach: Burn bootstrapper app for guided UX.
- Scope: Full v1 wizard coverage including allowlists and path controls, not only security-critical basics.
- Delivery constraint: Unattended/silent install parity is mandatory and blocks GA.
- Plan artifact preference: docs/plans/INSTALLER_WIZARD_PLAN.md as canonical repository plan file.

**Further Considerations**
1. Certificate strategy UX recommendation: default to generated CA-signed mode with explicit advanced option for user-provided cert; this minimizes first-run failure risk.
2. Path policy UX recommendation: include real-time existence/access validation warnings while still allowing override with explicit acknowledgment for advanced users.
3. Upgrade UX recommendation: provide clear preserve-vs-replace choices and a preview diff of effective settings before applying changes.
