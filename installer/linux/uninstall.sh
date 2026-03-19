#!/usr/bin/env bash
# =============================================================================
# uninstall.sh — Adagio Machine Agent Linux uninstaller
#
# Usage:
#   sudo bash uninstall.sh [--purge]
#
# Options:
#   --purge   Also removes /etc/adagio-machine-agent (config and TLS cert).
#             Without --purge the configuration is left in place so a
#             reinstall preserves the API key and certificate.
# =============================================================================
set -euo pipefail

INSTALL_DIR="/opt/adagio-machine-agent"
CONFIG_DIR="/etc/adagio-machine-agent"
LOG_DIR="/var/log/adagio-machine-agent"
SERVICE_NAME="adagio-agent"
SERVICE_FILE="/lib/systemd/system/${SERVICE_NAME}.service"
PURGE=false

while [[ $# -gt 0 ]]; do
    case "$1" in
        --purge) PURGE=true; shift ;;
        *)
            echo "Unknown argument: $1" >&2; exit 1 ;;
    esac
done

if [[ "$(id -u)" -ne 0 ]]; then
    echo "This script must be run as root." >&2
    exit 1
fi

echo "==> Uninstalling Adagio Machine Agent..."

# Stop and disable the service
if systemctl is-active --quiet "${SERVICE_NAME}" 2>/dev/null; then
    systemctl stop "${SERVICE_NAME}"
    echo "    Service stopped."
fi
if systemctl is-enabled --quiet "${SERVICE_NAME}" 2>/dev/null; then
    systemctl disable "${SERVICE_NAME}"
    echo "    Service disabled."
fi
if [[ -f "${SERVICE_FILE}" ]]; then
    rm -f "${SERVICE_FILE}"
    systemctl daemon-reload
    echo "    Service unit removed."
fi

# Remove binaries
if [[ -d "${INSTALL_DIR}" ]]; then
    rm -rf "${INSTALL_DIR}"
    echo "    Binaries removed: ${INSTALL_DIR}"
fi

# Remove log directory
if [[ -d "${LOG_DIR}" ]]; then
    rm -rf "${LOG_DIR}"
    echo "    Logs removed: ${LOG_DIR}"
fi

# Optionally remove configuration
if [[ "${PURGE}" == "true" ]]; then
    if [[ -d "${CONFIG_DIR}" ]]; then
        rm -rf "${CONFIG_DIR}"
        echo "    Configuration purged: ${CONFIG_DIR}"
    fi
    # Remove service user
    if id "${SERVICE_NAME}" &>/dev/null; then
        userdel "${SERVICE_NAME}"
        echo "    User '${SERVICE_NAME}' removed."
    fi
else
    echo "    Configuration retained: ${CONFIG_DIR}"
    echo "    (Run with --purge to also remove configuration and TLS certificate.)"
fi

echo ""
echo "==> Adagio Machine Agent uninstalled."
