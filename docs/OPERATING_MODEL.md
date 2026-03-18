# Operating Model

This document defines the current supported operating model for AdagioAgent.
It is the baseline for productization, support policy, release decisions, and
deployment guidance.

---

## Product posture

AdagioAgent is currently positioned as an **admin-managed automation product
for controlled environments**.

- It is **not** positioned as general consumer software.
- It is intended for environments where the same team controls the VS Code
  extension, the machine-agent host, and the network path between them.
- The primary target user is an engineer, QA lead, or packaging/release
  specialist operating in a lab, VM farm, or enterprise-managed workstation
  environment.

---

## Supported deployment model

### Windows

Windows is the primary supported platform.

- Preferred deployment target: Windows VM or managed Windows host.
- Preferred installation path: MSI installer.
- Preferred runtime mode: Windows Service with HTTPS enabled.
- Preferred connectivity model: loopback or private-network access only.

### Linux

Linux support remains **preview** until parity, packaging, and operational
validation are completed.

- Linux may be used for controlled experiments.
- Linux is not yet the primary support target for production use.
- Claims of full Linux support should be deferred until service startup,
  accessibility prerequisites, and troubleshooting workflows are hardened.

---

## Network and security assumptions

- HTTPS is required by default.
- API key authentication is required by default.
- The service is assumed to run on a trusted machine controlled by the same
  organization that operates the VS Code extension.
- Public Internet exposure is out of scope for the current product stage.
- Secrets must not be committed to source control or baked into install media.

### Current supported connectivity

- `https://127.0.0.1:5443` on the target machine
- private-network deployment where the operator controls firewall rules,
  certificate trust, and API key distribution

### Not yet in scope

- multi-tenant shared hosting
- Internet-exposed control plane
- federated identity / SSO / OAuth
- per-user authorization model

---

## Service account model

The installer currently registers the agent as a Windows Service.

- The default service registration is appropriate for service hosting.
- UI automation often requires access to an interactive desktop session.
- In practice, operators may need to run the service under an interactive user
  account for GUI automation scenarios.

This is a product constraint, not just a deployment detail, and must remain
documented until a more robust session model is introduced.

---

## API contract posture

- `/api/v1/...` is the canonical supported API surface.
- Unversioned legacy routes remain compatibility aliases only.
- Health and readiness endpoints are part of the operational contract.
- Breaking API changes require a new major path such as `/api/v2/...`.

---

## Support boundaries

### In scope

- installation via MSI on Windows
- HTTPS and API-key secured local/private deployment
- process execution, artifact collection, and VS Code-driven automation flows
- versioned API compatibility within major version 1

### Out of scope for the current release stage

- unmanaged public deployment
- consumer self-service onboarding without admin control
- high-availability clustering
- long-term backward compatibility across multiple API majors
- full Linux production support commitments

---

## Immediate productization decisions

These decisions are now treated as active working assumptions:

1. Windows is the primary supported operating system.
2. Linux remains preview until explicitly promoted.
3. Private-network and loopback deployment are the only supported connectivity modes.
4. HTTPS and API key auth remain mandatory defaults.
5. `/api/v1` remains the only documented API entry point.
6. AdagioAgent is an admin-managed product for controlled environments.