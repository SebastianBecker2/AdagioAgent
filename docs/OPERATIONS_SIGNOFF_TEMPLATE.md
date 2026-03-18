## Operations Sign-Off Template

Use this template to record operational readiness sign-off for a release
candidate and pilot deployment window.

### Sign-off metadata

- Release version:
- Sign-off date (UTC):
- Environment scope:
- Release owner:
- Operations owner:
- Incident commander on duty:

### Release gates

- [ ] Version parity validated (backend, extension, installer mapping).
- [ ] CI pipeline passed including docs and support-bundle validation checks.
- [ ] Release preflight passed (`scripts/release-preflight.ps1`).
- [ ] Release checklist completed (`docs/RELEASING.md`).

### Pilot readiness checks

- [ ] Startup diagnostics pass in target pilot environment.
- [ ] Support-bundle workflow verified (collection + validation).
- [ ] Correlation-ID flow verified from backend response to extension message.
- [ ] Rollback rehearsal completed for current candidate.

### Required evidence checklist

Attach or link evidence artifacts:

- [ ] Support bundle path and `manifest.json` summary.
- [ ] Correlation trace example (extension message + backend log linkage).
- [ ] Rollback rehearsal record (version, steps, outcome).
- [ ] Upgrade validation checklist output.
- [ ] Incident owner role assignments for Sev-1/Sev-2 windows.

### Evidence file references

Fill concrete repository-relative paths for evidence used in this sign-off:

- Support bundle evidence path:
- Correlation trace evidence path:
- Rollback rehearsal evidence path:
- Upgrade validation evidence path:

If evidence is external, provide immutable storage location and access notes.

### Risks and mitigations

- Known risks:
- Mitigations in place:
- Open follow-ups:

### Final approval

- [ ] Release owner approval
- [ ] Operations owner approval
- [ ] Pilot operations owner approval

Notes:
