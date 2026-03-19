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

Linux support is **beta** as of v0.4.

- Preferred deployment target: Ubuntu 22.04 LTS or 24.04 LTS (x64) with a
  running desktop/graphical session (AT-SPI2 requires a live X11 or Wayland
  compositor and the `at-spi2-core` daemon).
- Preferred installation path: `.deb` package (built via
  `scripts/build-linux-deb.sh`) or manual `installer/linux/install.sh`.
- Preferred runtime mode: systemd system service (`adagio-agent.service`).
- Preferred connectivity model: loopback or private-network access only,
  HTTPS with a self-signed certificate (see `docs/LINUX_HTTPS_SETUP.md`).
- UI automation backend: AT-SPI2 via D-Bus (`Tmds.DBus.Protocol`).

**Prerequisites on the Linux host:**
- `at-spi2-core` package installed and daemon running
- `DBUS_SESSION_BUS_ADDRESS` reachable by the service user (the `install.sh`
  script sets `DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/<UID>/bus` in
  the systemd unit automatically)
- `libdbus-1-3` shared library
- `openssl` (for certificate generation during install)

**Known limitations in v0.4 beta:**
- `send-keys` and `press-hotkey` are not yet implemented on Linux
- AT-SPI2 requires a live interactive desktop session; headless/VNC is
  possible but requires extra AT-SPI2 configuration
- The `.deb` package is unsigned; add to a controlled private repository
  or install with `sudo dpkg -i` directly

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

On **Windows** the installer registers the agent as a Windows Service. On
**Linux** the `install.sh` script creates a dedicated `adagio-agent` system
user and registers a systemd service unit.

- The default service registration is appropriate for service hosting.
- UI automation often requires access to an interactive desktop session.
  On Windows, operators may need to run the service under an interactive user
  account. On Linux, the `DBUS_SESSION_BUS_ADDRESS` environment variable in
  the systemd unit must point to the graphical user's D-Bus session socket
  (configured automatically by `install.sh` for UID 1000; adjust with
  `--session-uid` for other users).

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
- installation via `.deb` package or `install.sh` on Ubuntu/Debian Linux
- HTTPS and API-key secured local/private deployment on Windows and Linux
- process execution, artifact collection, and VS Code-driven automation flows
- AT-SPI2 UI automation on Linux (beta)
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
2. Linux is **beta** — supported for controlled deployments on Ubuntu with a
   graphical session and AT-SPI2; full production commitments deferred pending
   field validation.
3. Private-network and loopback deployment are the only supported connectivity modes.
4. HTTPS and API key auth remain mandatory defaults.
5. `/api/v1` remains the only documented API entry point.
6. AdagioAgent is an admin-managed product for controlled environments.