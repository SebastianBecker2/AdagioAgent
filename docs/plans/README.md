# Plans Index And Implementation Status

This folder contains planning documents and the roadmap source of truth.

## Documents

- [PROJECT_ROADMAP.md](PROJECT_ROADMAP.md)
- [PRODUCTIZATION_PLAN.md](PRODUCTIZATION_PLAN.md)
- [INSTALLER_BOOTSTRAP_PLAN.md](INSTALLER_BOOTSTRAP_PLAN.md)
- [VERSIONING_PLAN.md](VERSIONING_PLAN.md)

## Current Implementation Status (March 2026)

This summary is derived from the plan documents above and cross-checked
against the current roadmap status in `PROJECT_ROADMAP.md`.

| Plan | Status | Notes |
|---|---|---|
| PROJECT_ROADMAP | In progress overall | Phase 4 (Sessions/Concurrency) marked completed; Phases 1-3 still in progress; Phase 5+ not started |
| PRODUCTIZATION_PLAN | Mostly complete | Progress tracker items are checked; remaining work aligns with roadmap items still marked in progress (distribution completion, Linux Beta promotion, and later-phase hardening) |
| INSTALLER_BOOTSTRAP_PLAN | Mostly complete | v1 baseline implemented; rollout progress item 6 (continuous adjacent-version CI coverage with reproducible baseline MSI source) remains pending; advanced v2 UX remains open |
| VERSIONING_PLAN | Complete for API v1 lifecycle | Phases 1-4 complete and deprecation policy documented; Phase 5.3 (remove legacy unversioned aliases on API v2 transition) intentionally pending |

## Interpretation

- No plan appears blocked.
- Open items are mostly policy/transition gates (for example API v2 cleanup,
  Linux Beta promotion criteria, and continuous adjacent-upgrade CI artifact
  sourcing), not core implementation gaps.
- The roadmap remains the canonical conflict resolver if any plan and execution
  details diverge.
