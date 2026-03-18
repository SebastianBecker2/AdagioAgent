## Installer Upgrade Rollback Checklist

Use this checklist when an upgrade introduces regression, startup failure, or degraded readiness in a controlled pilot environment.

### Preconditions

- You have local administrator permissions on the target machine.
- The previous known-good MSI artifact is available.
- Current diagnostics are captured before rollback.

### 1. Capture pre-rollback evidence

1. Run `Adagio Agent: Run Startup Diagnostics` from VS Code.
2. Run support bundle collection:

```powershell
.\scripts\collect-support-bundle.ps1 -ApiKey '<api-key>'
```

3. Archive installer logs and current service status.

### 2. Stop service and uninstall current build

```powershell
Stop-Service AdagioMachineAgent -ErrorAction SilentlyContinue
msiexec /x AdagioMachineAgentSetup.msi /quiet /norestart
```

Verify service removal:

```powershell
Get-Service AdagioMachineAgent -ErrorAction SilentlyContinue
```

### 3. Install known-good previous version

```powershell
msiexec /i .\AdagioMachineAgentSetup-<previous-version>.msi /quiet /norestart
```

### 4. Post-rollback validation

1. Confirm service is running and set to automatic startup.
2. Validate endpoint responses:
   - `/api/v1/health`
   - `/api/v1/ready`
   - `/api/v1/diagnostics/status`
3. Run extension startup diagnostics again.
4. Run support bundle collection after rollback and compare with pre-rollback bundle.

### 5. Incident closure data

Record in pilot incident log:

- Upgraded version and rolled-back version.
- Trigger condition and user impact.
- Root cause hypothesis.
- Temporary mitigation.
- Follow-up fix tracking item.
