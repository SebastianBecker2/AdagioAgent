#!/usr/bin/env bash
set -euo pipefail

OUTPUT_DIR="${1:-artifacts/linux}"
REPORT_PATH="${OUTPUT_DIR}/linux-ui-parity-smoke.txt"

mkdir -p "${OUTPUT_DIR}"

log() {
  printf '%s\n' "$1" | tee -a "${REPORT_PATH}"
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

sleep 1

xterm -title "AdagioParitySmoke" >/tmp/adagio-xterm.log 2>&1 &
XTERM_PID=$!
sleep 1

WINDOW_ID="$(xdotool search --onlyvisible --pid "${XTERM_PID}" | head -n 1 || true)"
if [[ -z "${WINDOW_ID}" ]]; then
  log "ERROR: Failed to resolve xterm window id for pid ${XTERM_PID}."
  exit 1
fi

log "Resolved xterm window id: ${WINDOW_ID}"

xdotool key --window "${WINDOW_ID}" --clearmodifiers ctrl+l
xdotool type --window "${WINDOW_ID}" --clearmodifiers --delay 1 "adagio-linux-parity-smoke"
log "Sent hotkey and text input to xterm window via xdotool."

log "Linux UI parity smoke completed successfully."
