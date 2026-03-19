#!/usr/bin/env bash
set -euo pipefail

OUTPUT_DIR="${1:-artifacts/linux}"
REPORT_PATH="${OUTPUT_DIR}/linux-ui-parity-smoke.txt"

mkdir -p "${OUTPUT_DIR}"

log() {
  printf '%s\n' "$1" | tee -a "${REPORT_PATH}"
}

fail() {
  log "ERROR: $1"
  if [[ -f /tmp/adagio-xvfb.log ]]; then
    log "Xvfb log tail:"
    tail -n 20 /tmp/adagio-xvfb.log | tee -a "${REPORT_PATH}" >/dev/null || true
  fi
  if [[ -f /tmp/adagio-xterm.log ]]; then
    log "xterm log tail:"
    tail -n 20 /tmp/adagio-xterm.log | tee -a "${REPORT_PATH}" >/dev/null || true
  fi
  exit 1
}

: > "${REPORT_PATH}"
log "Linux UI parity smoke started: $(date -u +"%Y-%m-%dT%H:%M:%SZ")"

for cmd in Xvfb xdotool xterm; do
  if ! command -v "${cmd}" >/dev/null 2>&1; then
    log "ERROR: Required command '${cmd}' is missing."
    exit 1
  fi
  log "Found dependency: ${cmd}"
done

export DISPLAY=:99
Xvfb :99 -screen 0 1280x720x24 >/tmp/adagio-xvfb.log 2>&1 &
XVFB_PID=$!

cleanup() {
  if [[ -n "${XTERM_PID:-}" ]] && kill -0 "${XTERM_PID}" >/dev/null 2>&1; then
    kill "${XTERM_PID}" || true
  fi
  if kill -0 "${XVFB_PID}" >/dev/null 2>&1; then
    kill "${XVFB_PID}" || true
  fi
}
trap cleanup EXIT

# Wait for Xvfb to become reachable on DISPLAY.
for attempt in {1..30}; do
  if ! kill -0 "${XVFB_PID}" >/dev/null 2>&1; then
    fail "Xvfb exited before becoming ready."
  fi

  if xdotool getdisplaygeometry >/dev/null 2>&1; then
    log "Xvfb display is ready after ${attempt} attempt(s)."
    break
  fi

  if [[ "${attempt}" -eq 30 ]]; then
    fail "Timed out waiting for Xvfb display readiness."
  fi

  sleep 0.5
done

xterm -title "AdagioParitySmoke" >/tmp/adagio-xterm.log 2>&1 &
XTERM_PID=$!

if ! kill -0 "${XTERM_PID}" >/dev/null 2>&1; then
  fail "xterm exited immediately after launch."
fi

# xterm window creation can be slightly delayed; retry resolution.
WINDOW_ID=""
for attempt in {1..30}; do
  WINDOW_ID="$(xdotool search --onlyvisible --pid "${XTERM_PID}" | head -n 1 || true)"
  if [[ -n "${WINDOW_ID}" ]]; then
    log "Resolved xterm window id on attempt ${attempt}: ${WINDOW_ID}"
    break
  fi

  if ! kill -0 "${XTERM_PID}" >/dev/null 2>&1; then
    fail "xterm exited before a visible window could be resolved."
  fi

  if [[ "${attempt}" -eq 30 ]]; then
    fail "Failed to resolve xterm window id for pid ${XTERM_PID}."
  fi

  sleep 0.5
done

if [[ -z "${WINDOW_ID}" ]]; then
  fail "Failed to resolve xterm window id for pid ${XTERM_PID}."
fi

xdotool key --window "${WINDOW_ID}" --clearmodifiers ctrl+l
xdotool type --window "${WINDOW_ID}" --clearmodifiers --delay 1 "adagio-linux-parity-smoke"
log "Sent hotkey and text input to xterm window via xdotool."

log "Linux UI parity smoke completed successfully."
