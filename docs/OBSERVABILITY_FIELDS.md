## Observability Field Reference

This reference defines core structured logging fields used by the machine-agent
request/diagnostics pipeline.

### Core request fields

- `Method`: HTTP method (`GET`, `POST`, ...).
- `Path`: request path after middleware rewriting.
- `StatusCode`: final HTTP response code.
- `DurationMs`: total request processing duration in milliseconds.
- `CorrelationId`: correlation token used across request logs and user-visible
  extension errors.

### Lifecycle and cleanup fields

- `Reason`: reason for lifecycle action (`ApplicationStopping`, `ProcessService disposal`, etc.).
- `TerminatedProcesses`: number of running tracked processes terminated during
  cleanup.
- `RunningAfter`: remaining running process count after cleanup attempt.
- `Pruned`: count of exited process entries removed from tracking.

### Error contract fields

- `error`: stable high-level failure message for users and support workflows.
- `detail`: optional diagnostic detail (available in trusted/debug contexts).
- `correlationId`: request-level token to join extension symptoms with backend
  logs.

### Correlation ID semantics

- Incoming `X-Correlation-ID` is accepted when provided.
- If not provided, server trace identifier is used.
- `X-Correlation-ID` is echoed in every response.
- The same identifier should appear in:
  - backend request-completion logs
  - backend exception logs
  - API error payload (`correlationId`)
  - extension error/warning output

### Usage guidance

- Treat field names as contract-like for support tooling and incident timelines.
- Changes to field names or semantics require documentation updates and release
  notes.
