# AdagioAgent — Project Roadmap

This document is the single source of truth for the project's long-term
direction. It answers three questions at a glance: where we are, where we are
going, and what we believe success looks like.

It is not a sprint backlog. Items here represent commitments to capability
areas, not individual tasks. Each phase below will be broken down into
concrete issues and milestones as it approaches.

---

## Vision

AdagioAgent provides a reliable, secure bridge between GitHub Copilot and a
machine agent running inside a managed host — Windows VM, Linux VM, or service
host. Its purpose is to give AI assistants a safe, auditable way to act on a
real machine: launch processes, read and write files, interact with UI elements,
and collect diagnostics.

The long-term goal is for AdagioAgent to be the reference implementation of a
"managed local agent" pattern for Copilot-powered developer tooling. It should
be easy to install, easy to trust, and easy to audit.

---

## Current State (v0.2.x)

| Area | Status | Notes |
|---|---|---|
| Core process execution | Stable | run, run-and-collect, run-and-assert workflows |
| File operations | Stable | copy, read, tail, list, exists, assert |
| UI automation — Windows | Beta | FlaUI/UIA3; interactive session constraint documented |
| UI automation — Linux | Preview | AT-SPI2/X11 core command set implemented; parity validation and Beta promotion criteria remain |
| Screenshot / visual inspection | Beta | base64 PNG capture, no OCR |
| Security model | Stable | HTTPS + API key, path allowlist, SecurityPolicy |
| Installer | Stable | WiX v6 MSI with bootstrap + preflight diagnostics and fail-fast service validation |
| CI/CD pipeline | Stable | AppVeyor + GitHub Actions (Linux CI and tag-based release workflow) |
| Release governance | Stable | Sign-off, evidence index, promotion gate |
| Linux packaging | In progress | `.deb` build script, Linux service unit, install/uninstall scripts, and release artifacts |
| VS Code Marketplace publish | In progress | VSIX release artifact workflow in place; Marketplace publication pending |
| Quick-start onboarding | Stable | `docs/QUICKSTART.md` added for Windows and Linux first-run flow |
| Multi-agent / multi-session | Not started | single connection model only |
| Plugin / extension model | Not started | all commands compiled-in |

---

## Implementation Snapshot (March 2026)

This snapshot tracks meaningful roadmap execution without converting the
roadmap into a sprint backlog.

| Phase | Progress | Notes |
|---|---|---|
| Phase 1 — Core Reliability | In progress | Structured diagnostics, controller-level 4xx/5xx contract normalization, and expanded error-path regression coverage are in place; remaining work is deeper cancellation semantics and final edge-case hardening |
| Phase 2 — Linux Parity | In progress (substantial) | Linux HTTPS setup docs, systemd unit/install scripts, `.deb` packaging script, Linux CI/release legs, Linux `send-keys`/`press-hotkey` support, and parity evidence matrix implemented; operating-model promotion to Linux Beta still pending |
| Phase 3 — Distribution | In progress | GitHub Release draft workflow with MSI/VSIX/.deb artifacts and quick-start guide implemented; Marketplace publication and final README distribution/dev split still pending |
| Phase 4+ | Not started | Session model, security maturity expansion, extensibility model, and GA commitments remain planned work |

Recent phase-aligned completions:

1. Linux CI workflow added for build/test/package validation on Ubuntu.
2. Release workflow added to attach MSI, VSIX, and `.deb` artifacts to tagged releases.
3. Linux packaging assets added (`installer/linux/*.service`, install/uninstall scripts, `.deb` build script).
4. Quick-start guide added for first-run setup on Windows and Linux.
5. Controller extension now surfaces machine-agent `errorCode` and `remediationHint` details in client-side error output to improve LLM/operator troubleshooting.
6. Automation controller validation failures now return standardized structured payloads with `VALIDATION_FAILED`/`PATH_NOT_ALLOWED` codes and actionable remediation hints across process, UI, and file endpoints.
7. Missing file/directory 404 responses now emit `PATH_NOT_FOUND`, and integration tests now assert structured parity for versioned/legacy route errors and path-not-found responses.

---

## Guiding Principles

1. **Operator trust before end-user growth.** Grow only as fast as the
   security and operational model can support. Never ship a feature that
   removes a safety check.

2. **Auditability by default.** Every significant action should leave a trail
   observable by an administrator and legible by an LLM.

3. **Fail explicitly.** Errors should name the exact problem and the exact
   remediation. Silent failures and unhelpful 500 responses are bugs.

4. **Portable correctness.** Behavior on Windows and Linux should be
   equivalent where the platforms support it. Platform-specific code must
   be isolated and documented.

5. **Minimal footprint.** The machine agent process should not accumulate
   state beyond what the current operation requires. Avoid in-process
   caches, ambient session state, and implicit global configuration.

---

## Phases

### Phase 1 — Core Reliability (target: v0.3)

Harden the existing feature set before expanding it. The goal is that every
supported command works correctly under realistic conditions (process crashes,
timeout races, file permission errors) and returns structured, actionable
diagnostics in every failure case.

| # | Item |
|---|---|
| 1.1 | Structured error response model: all 4xx/5xx responses include `errorCode`, `message`, and `remediationHint` fields |
| 1.2 | Timeout and cancellation: all long-running operations (`wait-for-exit`, `wait-for-element`) honour a caller-supplied deadline |
| 1.3 | Process lifecycle correctness: tracked process table does not leak across requests; stale entries are pruned on `/ready` |
| 1.4 | Windows UI automation: missing-process and inaccessible-window errors are classified and returned as named error codes, not unhandled exceptions |
| 1.5 | Regression test suite for error paths: xUnit tests cover the main failure branches for process, file, and UI operations |
| 1.6 | Extension: surface machine-agent error codes in Copilot tool output so the LLM can reason about them |

### Phase 2 — Linux Parity (target: v0.4)

Promote Linux from Preview to Beta. Parity means: an operator can install,
run, and troubleshoot the agent on a GUI-capable Linux host with the same
procedure as on Windows.

| # | Item |
|---|---|
| 2.1 | AT-SPI2 implementation: `get-ui-tree`, `element-state`, `click`, `type`, `send-keys`, `wait-for-element` on Linux |
| 2.2 | HTTPS on Linux: document or automate self-signed certificate setup for Kestrel on Linux |
| 2.3 | systemd service unit: provide a `adagio-agent.service` template and install script |
| 2.4 | `.deb` package: build and test a Debian/Ubuntu package alongside the Windows MSI |
| 2.5 | Linux CI worker: add an AppVeyor or GitHub Actions Linux build leg that runs the installer integration tests |
| 2.6 | Update OPERATING_MODEL.md to reflect Linux Beta status once 2.1–2.5 are done |

### Phase 3 — Distribution (target: v0.5)

Make it possible for a user to find, install, and start using AdagioAgent
without reading the source code. This phase makes the project usable outside
a controlled lab.

| # | Item |
|---|---|
| 3.1 | VS Code Marketplace: package and publish the controller extension (`vsce package`, publisher setup, marketplace listing) |
| 3.2 | GitHub Releases: attach MSI and (later) .deb artifacts to each tagged release via CI |
| 3.3 | One-command bootstrap: `bootstrap-agent.ps1` should handle the full first-run flow (cert, API key, service start) with no manual steps |
| 3.4 | Quick-start guide: a `docs/QUICKSTART.md` that takes a user from zero to first successful Copilot command in under 10 minutes |
| 3.5 | Extension activation telemetry: log first-activation and first-successful-command to the extension output channel (local only, no external calls) |
| 3.6 | README restructure: separate "I want to use this" from "I want to develop this" sections |

### Phase 4 — Session and Concurrency (target: v0.6)

Support more than one concurrent Copilot session connecting to the same agent,
and model the difference between a pipeline run and an interactive session.

| # | Item |
|---|---|
| 4.1 | Session tokens: clients obtain a session ID at connect time; process tracking and artifact collection are scoped to an active session |
| 4.2 | Concurrent session limit: operators configure a max-session cap; excess connections receive a structured `agentBusy` response |
| 4.3 | Session heartbeat and expiry: idle sessions are reclaimed after a configurable timeout; in-progress operations are cleanly cancelled |
| 4.4 | Extension: replace the static `vmAgentUrl` model with a session lifecycle — connect on first tool use, reconnect on session loss |
| 4.5 | Diagnostics endpoint update: `/diagnostics/status` reports active session count and session age |

### Phase 5 — Security Maturity (target: v0.7)

Raise the security posture to the level expected for a product in a regulated
or security-conscious enterprise environment.

| # | Item |
|---|---|
| 5.1 | mTLS support: optionally require the client (VS Code extension) to present a certificate; validate against a pinned issuer |
| 5.2 | Audit log: every mutation (process start, file write, UI interaction) is appended to a local audit log with caller IP, timestamp, and command |
| 5.3 | Rate limiting: operators configure per-second and per-minute request limits; excess requests receive a `429` with retry-after |
| 5.4 | Scope-based API key model: API keys can be scoped to read-only, execute-only, or full-access |
| 5.5 | Automated `dotnet list package --vulnerable` scan: fail the CI build on any high/critical CVE in direct dependencies |
| 5.6 | Rotation runbook: `docs/CERTIFICATE_ROTATION_RUNBOOK.md` and automated rehearsal script |

### Phase 6 — Extensibility (target: v0.8)

Allow operators and contributors to add new commands without forking the
source.

| # | Item |
|---|---|
| 6.1 | Command plugin interface: define an `ICommandHandler` contract and a discovery mechanism (assembled plugins in a `plugins/` directory) |
| 6.2 | Command schema registration: plugins declare their REST surface and VS Code tool descriptors via a JSON manifest |
| 6.3 | Security policy integration: plugin commands can declare required path policies; SecurityPolicy enforces them uniformly |
| 6.4 | Plugin signing (optional): operators can require plugins to be signed before loading |
| 6.5 | Example plugin: a reference implementation bundled as a separate project in the repo |

### Phase 7 — General Availability (v1.0)

v1.0 signals that the API is stable, the security model is production-ready,
and the project commits to a backward-compatibility policy.

| # | Item |
|---|---|
| 7.1 | API stability declaration: all `/v1/` routes are stable and covered by a deprecation policy |
| 7.2 | Breaking change policy: documented in CONTRIBUTING.md and enforced by a CI schema diff check |
| 7.3 | Accessibility audit: UI automation tools are verified against current WCAG / platform accessibility APIs |
| 7.4 | Full Linux GA: Linux parity confirmed, packaging automated, Linux in the primary CI matrix |
| 7.5 | Performance baseline: latency p50/p99 benchmarks for core command categories; regression gate in CI |
| 7.6 | Security review: external or self-conducted threat-model review; findings documented and resolved or accepted |
| 7.7 | Marketplace listing: curated screenshots, feature summary, and first-week activation target set |

---

## Milestone Summary

| Milestone | Theme | Key deliverable |
|---|---|---|
| v0.3 | Core reliability | Structured errors; timeout/cancel; regression tests for error paths |
| v0.4 | Linux parity | AT-SPI2 complete; systemd unit; .deb package; Linux CI leg |
| v0.5 | Distribution | Marketplace publish; GitHub Release assets; quick-start guide |
| v0.6 | Sessions | Session tokens; concurrency cap; session-scoped process tracking |
| v0.7 | Security | mTLS; audit log; rate limiting; scope-based keys |
| v0.8 | Extensibility | Plugin model; manifest-driven command registration |
| v1.0 | GA | Stable API; backward-compat policy; full Linux GA; security review |

---

## Out of Scope (Intentionally Deferred)

These items are frequently discussed but are not planned for any current
phase. They should not be pursued without an explicit decision to revise
this roadmap.

- **Public Internet exposure / cloud relay**: the current security model
  assumes a private network. A cloud relay requires a materially different
  threat model and auth design.
- **Multi-tenant SaaS**: outside the admin-managed deployment posture.
- **Mobile / browser targets**: the machine agent is a server process; mobile
  is not a near-term platform target.
- **AI-generated plugin authoring**: interesting but not a core product
  capability until the plugin model (Phase 6) is stable.
- **Billing / licensing infrastructure**: this is an open-source project;
  enterprise licensing is out of scope unless the project posture changes.

---

## Roadmap Revision Policy

This document is reviewed at the start of each release cycle. Changes require
a pull request with a rationale comment. Additions to "Out of Scope" require
explicit justification. Items may be re-ordered between phases based on
community feedback or operational findings, but the v1.0 criteria (Phase 7)
serve as the fixed target.
