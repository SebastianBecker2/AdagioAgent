# AdagioAgent Quick-Start Guide

Get from zero to your first Copilot-driven automation command in under 10 minutes.

## Beginner quick start (first successful run)

If this is your first automation project, start here.

### What you are building

- The machine agent runs on your VM (or your own machine for local testing).
- The VS Code extension sends commands to that agent.
- Copilot can then launch apps, click UI elements, and collect logs.

### Fastest beginner path (Windows, same machine)

1. Install `AdagioMachineAgentSetup.msi` from GitHub Releases.
2. Open this file and copy your API key:
   - `C:\ProgramData\AdagioMachineAgent\bootstrap-secrets.json`
3. Install the VS Code extension from the `.vsix` file in GitHub Releases.
4. In VS Code settings, set:
   - `adagioAgent.vmAgentUrl` = `https://127.0.0.1:5443/api/v1`
   - `adagioAgent.vmAgentApiKey` = your API key from step 2
5. Run command palette action: **Adagio Agent: Run Startup Diagnostics**
6. In Copilot Chat, try:
   - "Start notepad.exe and tell me the PID"

If that works, continue with the full steps below for Linux, remote VM setup, and troubleshooting.

## Prerequisites

| Requirement | Notes |
|---|---|
| Windows 10/11 or Ubuntu 22.04+ target VM | The VM where automation runs |
| VS Code 1.85+ with GitHub Copilot | Your developer workstation |
| Administrator (Windows) or `sudo` (Linux) on the target VM | Required to install the agent service |

---

## Step 1 — Install the machine agent on the target VM

### Windows

1. Download `AdagioMachineAgentSetup.msi` from [GitHub Releases](https://github.com/SebastianBecker2/AdagioAgent/releases).

2. Install silently (run as administrator):
   ```powershell
   msiexec /i AdagioMachineAgentSetup.msi /quiet
   ```
   The MSI automatically:
   - Runs `bootstrap-agent.ps1` to generate a self-signed TLS certificate and API key.
   - Includes `localhost`, the VM hostname, and detected non-loopback IPv4 addresses in the certificate SAN.
   - Writes credentials to `appsettings.json`.
   - Starts the `AdagioMachineAgent` Windows service on port 5443.

3. Retrieve your API key:
   ```powershell
   Get-Content "C:\ProgramData\AdagioMachineAgent\bootstrap-secrets.json"
   ```
   Copy the `apiKey` value — you will need it in Step 3.
   You can also use `httpsCaCertificatePemPath` from the same file as
   `adagioAgent.vmAgentCaCertPath` in VS Code so the extension trusts the VM
   certificate without importing it into the Windows trust store.

#### Silent install parity (wizard-equivalent inputs)

Use MSI properties to provide explicit certificate/API key values in unattended installs:

```powershell
msiexec /i AdagioMachineAgentSetup.msi /quiet /qn \
   ADAGIO_CERT_MODE=Provided \
   ADAGIO_PROVIDED_CERT_PATH="C:\ProgramData\AdagioMachineAgent\tls\agent.pfx" \
   ADAGIO_PROVIDED_CERT_PASSWORD="<pfx-password>" \
   ADAGIO_API_KEY_MODE=Provided \
   ADAGIO_PROVIDED_API_KEY="<api-key>"
```

Supported values:
- `ADAGIO_CERT_MODE`: `GeneratedCa` (default), `GeneratedLeaf`, `Provided`
- `ADAGIO_API_KEY_MODE`: `Generate` (default), `Provided`

You can also provide a response file path for deterministic unattended configuration:

```powershell
msiexec /i AdagioMachineAgentSetup.msi /quiet /qn \
   ADAGIO_RESPONSE_FILE_PATH="C:\Install\adagio-response.json"
```

Generate that response file with discovery-aware defaults:

```powershell
.\scripts\generate-installer-response-file.ps1 -OutputPath C:\Install\adagio-response.json
```

Use `-NonInteractive` to produce a deterministic file from CLI values only.

Example response file:

```json
{
   "security": {
      "certificateMode": "GeneratedCa",
      "apiKeyMode": "Provided",
      "providedApiKey": "replace-me",
      "requireHttps": true,
      "requireApiKey": true
   },
   "network": {
      "urls": "https://10.0.0.2:5443",
      "allowedHosts": "10.0.0.2;agent-host"
   },
   "agentOptions": {
      "allowedExecutablePaths": ["C:\\Tools"],
      "allowedWritablePaths": ["C:\\Logs"],
      "allowedReadablePaths": ["C:\\Logs", "C:\\Tools"]
   }
}
```

Precedence order for overlapping values is: explicit MSI/wizard properties, response file, existing appsettings values, bootstrap defaults.

#### One-command bootstrap (if you prefer manual setup)

If you cloned the repo and want to run bootstrap directly:
```powershell
.\scripts\bootstrap-agent.ps1 `
    -WriteToAppSettings `
    -WriteSecretHandoff `
   -DnsNames $env:COMPUTERNAME, "localhost" `
   -IpAddresses "127.0.0.1", "192.168.178.59" `
    -TrustCertificate `
    -StartService
```
`-TrustCertificate` installs the generated cert into `LocalMachine\Root` so HTTPS connections from the same machine are trusted.  
`-StartService` restarts the `AdagioMachineAgent` service automatically.
If VS Code connects from another machine, include the VM hostname and VM IP in `-DnsNames` and `-IpAddresses` so the certificate matches `adagioAgent.vmAgentUrl`.

### Linux (Ubuntu)

1. Download `adagio-machine-agent_*.deb` from [GitHub Releases](https://github.com/SebastianBecker2/AdagioAgent/releases).

2. Install:
   ```bash
   sudo dpkg -i adagio-machine-agent_*.deb
   sudo systemctl enable --now adagio-machine-agent
   ```

3. Run bootstrap:
   ```bash
   sudo /opt/adagio-machine-agent/bootstrap-agent.sh
   ```

4. Retrieve your API key:
   ```bash
   sudo cat /etc/adagio-machine-agent/bootstrap-secrets.json
   ```

For TLS trust on Linux, see [docs/LINUX_HTTPS_SETUP.md](LINUX_HTTPS_SETUP.md).

---

## Step 2 — Install the VS Code extension

**Option A — VS Code Marketplace** (once published):  
Open VS Code, go to Extensions (`Ctrl+Shift+X`), search for **Adagio Agent Controller**, and click Install.

**Option B — from a VSIX file:**
1. Download `adagio-agent-controller-*.vsix` from [GitHub Releases](https://github.com/SebastianBecker2/AdagioAgent/releases).
2. In VS Code: `Extensions` → `...` menu → **Install from VSIX…** → select the file.

---

## Step 3 — Configure the extension

Open VS Code Settings (`Ctrl+,`) and configure:

| Setting | Value | Example |
|---|---|---|
| `adagioAgent.vmAgentUrl` | URL of the machine agent | `https://192.168.1.50:5443/api/v1` |
| `adagioAgent.vmAgentApiKey` | API key from Step 1 | `abc123...` |

> **Loopback testing:** If VS Code and the agent are on the same machine, use `https://127.0.0.1:5443/api/v1`.

---

## Step 4 — Verify the connection

1. Open the Command Palette (`Ctrl+Shift+P`).
2. Run **Adagio Agent: Run Startup Diagnostics**.
3. The status bar at the bottom left should change to **✓ Adagio: Ready**.

If the status shows **✗ Adagio: Offline**, see [Troubleshooting](#troubleshooting) below.

---

## Step 5 — Your first Copilot command

Open GitHub Copilot Chat and try:

> `Start notepad.exe on the VM and tell me its PID.`

Copilot will invoke the `adagioAgent_runExecutable` tool and return the process ID from the remote VM.

Other things to try:
- `"Take a screenshot of the VM desktop."`
- `"List the contents of C:\Users on the VM."`
- `"Run C:\MyTests\setup.exe on the VM and collect any .log files created."`

---

## Troubleshooting

### Status bar shows `✗ Adagio: Offline`

| Cause | Fix |
|---|---|
| Wrong URL | Check `adagioAgent.vmAgentUrl` — include `/api/v1` path |
| Service not running | Run `Get-Service AdagioMachineAgent` (Windows) or `systemctl status adagio-machine-agent` (Linux) |
| Firewall blocking port 5443 | Open inbound TCP 5443 on the target VM |
| TLS certificate not trusted | On Windows run bootstrap with `-TrustCertificate`; on Linux see [LINUX_HTTPS_SETUP.md](LINUX_HTTPS_SETUP.md) |

### `401 Unauthorized` in the Adagio Agent output channel

The API key in VS Code settings does not match the one on the VM.  
Re-read `bootstrap-secrets.json` on the VM and update `adagioAgent.vmAgentApiKey`.

### Opening the diagnostics output channel

Run **Adagio Agent: Open Diagnostics Output** from the Command Palette to see the full log of all agent requests, responses, and any errors.

The output channel also records local-only extension telemetry markers for:
- first extension activation
- first successful Adagio tool invocation

These markers stay inside the VS Code output channel and are not sent to any external service.

---

## Next steps

- Read the full [README](../README.md) for architecture details and developer setup.
- See [docs/OPERATIONS_RUNBOOK.md](PILOT_RUNBOOK.md) for multi-VM fleet management.
- See [docs/RELEASING.md](RELEASING.md) for how to publish a new release.
