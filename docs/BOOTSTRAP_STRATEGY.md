# Bootstrap Strategy

This document records the chosen bootstrap approach for certificate and API-key
provisioning during productization.

## Decision

AdagioAgent will use a **first-service-start bootstrap strategy** instead of
installer-time custom actions for initial security provisioning.

### Why this strategy

1. It keeps installer behavior simpler and less brittle than WiX custom action
   logic for certificate/key generation.
2. It allows the service startup path to produce actionable diagnostics through
   `/ready` and startup logs.
3. It keeps provisioning logic in the backend codebase where it can be tested
   with xUnit/integration tests instead of MSI-only verification.
4. It supports future environment-specific provisioning (enterprise PKI,
   managed secrets) without rewriting installer UX each time.

## Scope of bootstrap work

The first-service-start path will:

- validate transport requirements (HTTPS + certificate configuration)
- validate API key requirements
- validate core agent policy settings (allowed paths)
- expose failures through startup errors and readiness issues

## Near-term implementation sequence

1. Keep explicit fail-fast validation in startup (`Program.cs` + `SecurityPolicy`).
2. Expand `/ready` issue reporting to surface actionable config defects.
3. Add extension startup connection check against `/ready` to provide early
   feedback to operators.
4. Add optional provisioning helper scripts later (outside MSI custom actions)
   if teams need assisted setup.

## Deferred items

- Automated certificate generation and secure storage are still planned and not
  fully implemented yet.
- API key rotation and secret vault integration are planned and not fully
  implemented yet.