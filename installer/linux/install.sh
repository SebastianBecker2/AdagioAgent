#!/usr/bin/env bash
# =============================================================================
# install.sh — Adagio Machine Agent Linux installer
#
# Usage:
#   sudo bash install.sh [--publish-dir <path>] [--session-uid <uid>]
#
# Options:
#   --publish-dir <path>   Path to the dotnet publish output directory.
#                          Default: script directory (installer/linux/../publish)
#   --session-uid <uid>    UID of the graphical-session user that owns the
#                          D-Bus session bus.  Default: 1000
#
# What this script does:
#   1. Creates a dedicated 'adagio-agent' system user and group.
#   2. Installs the agent binary to /opt/adagio-machine-agent/.
#   3. Generates a self-signed TLS certificate (PFX) and a random API key.
#   4. Writes /etc/adagio-machine-agent/appsettings.json with the generated
#      secrets (mode 0640, owned by root:adagio-agent).
#   5. Installs the systemd unit file and reloads the daemon.
#   6. Enables and starts the service.
#
# Prerequisites:
#   • openssl (for certificate generation)
#   • A running D-Bus session bus for the desktop user (AT-SPI2 automation)
# =============================================================================
set -euo pipefail

INSTALL_DIR="/opt/adagio-machine-agent"
CONFIG_DIR="/etc/adagio-machine-agent"
LOG_DIR="/var/log/adagio-machine-agent"
SERVICE_NAME="adagio-agent"
SERVICE_FILE="/lib/systemd/system/${SERVICE_NAME}.service"
TLS_DIR="${CONFIG_DIR}/tls"
CERT_PFX="${TLS_DIR}/agent.pfx"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PUBLISH_DIR="${SCRIPT_DIR}"
SESSION_UID="1000"

# ── Argument parsing ──────────────────────────────────────────────────────────
while [[ $# -gt 0 ]]; do
    case "$1" in
        --publish-dir)
            PUBLISH_DIR="$2"; shift 2 ;;
        --session-uid)
            SESSION_UID="$2"; shift 2 ;;
        *)
            echo "Unknown argument: $1" >&2; exit 1 ;;
    esac
done

# ── Root check ────────────────────────────────────────────────────────────────
if [[ "$(id -u)" -ne 0 ]]; then
    echo "This script must be run as root." >&2
    exit 1
fi

echo "==> Installing Adagio Machine Agent..."

# ── Create service user ───────────────────────────────────────────────────────
if ! id "${SERVICE_NAME}" &>/dev/null; then
    useradd --system --no-create-home --shell /usr/sbin/nologin "${SERVICE_NAME}"
    echo "    Created system user '${SERVICE_NAME}'."
fi

# ── Create directories ────────────────────────────────────────────────────────
install -d -m 0755 "${INSTALL_DIR}"
install -d -m 0750 -o root -g "${SERVICE_NAME}" "${CONFIG_DIR}"
install -d -m 0750 -o root -g "${SERVICE_NAME}" "${TLS_DIR}"
install -d -m 0750 -o "${SERVICE_NAME}" -g "${SERVICE_NAME}" "${LOG_DIR}"

# ── Copy binaries ─────────────────────────────────────────────────────────────
echo "==> Copying binaries from '${PUBLISH_DIR}'..."
cp -r "${PUBLISH_DIR}/." "${INSTALL_DIR}/"
chmod 0755 "${INSTALL_DIR}/AdagioMachineAgent"
chown -R root:root "${INSTALL_DIR}"
chmod -R a+rX "${INSTALL_DIR}"

# ── Generate TLS certificate ──────────────────────────────────────────────────
if [[ ! -f "${CERT_PFX}" ]]; then
    echo "==> Generating self-signed TLS certificate..."
    TMP_KEY=$(mktemp)
    TMP_CERT=$(mktemp)
    CERT_PASSWORD=$(openssl rand -base64 24 | tr -d '\n')

    openssl req -x509 \
        -newkey rsa:4096 \
        -keyout "${TMP_KEY}" \
        -out "${TMP_CERT}" \
        -days 3650 \
        -nodes \
        -subj "/CN=adagio-machine-agent/O=AdagioAgent" \
        -addext "subjectAltName=IP:127.0.0.1,DNS:localhost" \
        2>/dev/null

    openssl pkcs12 \
        -export \
        -out "${CERT_PFX}" \
        -inkey "${TMP_KEY}" \
        -in "${TMP_CERT}" \
        -passout "pass:${CERT_PASSWORD}" \
        2>/dev/null

    rm -f "${TMP_KEY}" "${TMP_CERT}"
    chmod 0640 "${CERT_PFX}"
    chown root:"${SERVICE_NAME}" "${CERT_PFX}"
    echo "    Certificate written to: ${CERT_PFX}"
else
    echo "==> Existing certificate retained: ${CERT_PFX}"
    # Read the existing password from appsettings if present
    CERT_PASSWORD=$(python3 -c "
import json, sys
try:
    d = json.load(open('${CONFIG_DIR}/appsettings.json'))
    print(d.get('SecurityOptions', {}).get('HttpsCertificatePassword', ''))
except: pass" 2>/dev/null || true)
fi

# ── Generate API key ──────────────────────────────────────────────────────────
if [[ -f "${CONFIG_DIR}/appsettings.json" ]]; then
    echo "==> Existing appsettings.json retained."
else
    echo "==> Generating API key and writing appsettings.json..."
    API_KEY=$(openssl rand -base64 32 | tr -d '\n/+=' | head -c 40)

    cat > "${CONFIG_DIR}/appsettings.json" <<JSON
{
  "Urls": "https://127.0.0.1:5443",
  "SecurityOptions": {
    "RequireHttps": true,
    "RequireApiKey": true,
    "ApiKey": "${API_KEY}",
    "ApiKeyHeaderName": "X-API-Key",
    "HttpsCertificatePath": "${CERT_PFX}",
    "HttpsCertificatePassword": "${CERT_PASSWORD}"
  },
  "AgentOptions": {
    "AllowedExecutablePaths": ["/usr/bin", "/usr/local/bin", "/opt"],
    "AllowedWritablePaths": ["/tmp"],
    "AllowedReadablePaths": ["/tmp", "/var/log"],
    "MaxConcurrentProcesses": 4,
    "ProcessTimeoutSeconds": 300
  }
}
JSON
    chmod 0640 "${CONFIG_DIR}/appsettings.json"
    chown root:"${SERVICE_NAME}" "${CONFIG_DIR}/appsettings.json"

    echo ""
    echo "============================================================"
    echo "  API Key (save this — it will not be shown again):"
    echo "  ${API_KEY}"
    echo "============================================================"
    echo ""
fi

# ── Install systemd service unit ──────────────────────────────────────────────
echo "==> Installing systemd service unit..."
# Patch the D-Bus session UID into the service file
sed "s|/run/user/1000/bus|/run/user/${SESSION_UID}/bus|g" \
    "${SCRIPT_DIR}/adagio-agent.service" > "${SERVICE_FILE}"
chmod 0644 "${SERVICE_FILE}"

# Ensure the agent picks up the config from /etc rather than the install dir
if [[ ! -f "${INSTALL_DIR}/appsettings.json" ]]; then
    ln -sf "${CONFIG_DIR}/appsettings.json" "${INSTALL_DIR}/appsettings.json"
fi

systemctl daemon-reload
systemctl enable "${SERVICE_NAME}"
systemctl restart "${SERVICE_NAME}"

echo ""
echo "==> Adagio Machine Agent installed and started."
echo "    Status:  systemctl status ${SERVICE_NAME}"
echo "    Logs:    journalctl -u ${SERVICE_NAME} -f"
echo "    Config:  ${CONFIG_DIR}/appsettings.json"
echo ""
