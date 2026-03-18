# Versioning Plan

## Goals

- Keep release flow simple and predictable.
- Allow machine-agent and controller-extension to evolve independently.
- Make API compatibility explicit and safe over time.

## Phase 1: Define Versioning Policy

1. [x] Use independent SemVer for each artifact:
- controller-extension (VSIX/npm)
- machine-agent (service binary/assembly)
- installer (MSI)
2. [x] Version the REST API by path using `/api/v1/...`.
3. [x] Define compatibility policy:
- extension supports API major 1
- agent publishes supported API major(s) in health
- breaking API changes require new major path (`/api/v2/...`)

## Phase 2: Introduce API Version Foundations (Backward Compatible)

1. [x] Add `/api/v1` route aliases for all existing endpoints.
2. [x] Keep current unversioned routes as compatibility aliases temporarily.
3. [x] Enrich health response with:
- `agentVersion`
- `apiVersion`
- optional compatibility hints (`minSupportedClientVersion`, `maxSupportedClientVersion`)

## Phase 3: Align Client and Server

1. [x] Update controller-extension to call `/api/v1/...` by default.
2. [x] Keep configuration-based override for transition windows.
3. [x] Align and test contracts between C# models and TypeScript schema.
3. [x] Add tests for:
- `/api/v1` routes
- legacy route compatibility
- health compatibility metadata

## Phase 4: Release and Source of Truth

1. [x] Make machine-agent version explicit in project metadata.
2. [x] Keep installer version tied to release process rules.
3. [x] Add release checklist:
- bump versions
- run tests
- update compatibility matrix
- create release tags

## Phase 5: Deprecation and Cleanup

1. [x] Mark unversioned routes as deprecated in docs.
2. [x] Define removal timeline (for example next major).
3. [ ] Remove deprecated aliases when moving to API v2.

## Deliverables

1. [x] README section: Versioning and Compatibility
2. [x] `/api/v1` route availability
3. [x] Health contract with API version metadata
4. [x] Compatibility matrix in docs
5. [x] Tests for versioned routes and compatibility behavior
