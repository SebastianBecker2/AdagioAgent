# Contributing

## Prerequisites

- .NET 8 SDK
- Node.js and npm
- Windows environment for full installer and Windows UI automation workflows

## Development Workflow

1. Create a focused branch from `main`.
2. Keep changes scoped to one slice or feature.
3. Add or update tests for behavior changes.
4. Update documentation when endpoints, commands, or operator workflows change.

## Build And Test

Backend tests:

```powershell
dotnet test
```

Extension tests:

```powershell
Set-Location controller-extension
npm test -- --run
```

Run both suites before opening a pull request.

## Pull Request Expectations

- Clear problem statement and scope.
- Summary of implementation and behavior changes.
- Test evidence (which suites were run and outcomes).
- Documentation updates when applicable.

## Coding Standards

- Prefer small, reviewable commits.
- Preserve backward compatibility for documented API contracts unless a breaking change is explicitly planned.
- Keep security posture intact: HTTPS and API-key assumptions should not be weakened unintentionally.

## Release And Versioning Notes

- Follow the documented release process in `docs/RELEASING.md`.
- Update `CHANGELOG.md` in the same PR for user-visible changes.
- Keep version compatibility information aligned across backend, extension, installer, and docs.
