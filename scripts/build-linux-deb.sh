#!/usr/bin/env bash
# =============================================================================
# build-linux-deb.sh — Build an unsigned Debian package for Adagio Machine Agent
#
# Usage (from repository root):
#   bash scripts/build-linux-deb.sh [--version <ver>] [--output-dir <dir>]
#
# Options:
#   --version <ver>      Package version string (e.g. "0.4.0").
#                        Default: read from machine-agent/AdagioMachineAgent.csproj.
#   --output-dir <dir>   Directory to write the .deb file.
#                        Default: ./artifacts/linux
#
# Prerequisites:
#   • .NET 8 SDK  (dotnet)
#   • dpkg-deb    (apt-get install dpkg)
#
# The script:
#   1. Publishes the machine-agent as a self-contained linux-x64 binary.
#   2. Assembles a DEBIAN/ control tree in a temp directory.
#   3. Calls dpkg-deb --build to produce the .deb artifact.
# =============================================================================
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SCRIPT_DIR="${REPO_ROOT}/scripts"
CSPROJ="${REPO_ROOT}/machine-agent/AdagioMachineAgent.csproj"
LINUX_INSTALLER_DIR="${REPO_ROOT}/installer/linux"
OUTPUT_DIR="${REPO_ROOT}/artifacts/linux"
VERSION=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --version)
            VERSION="$2"; shift 2 ;;
        --output-dir)
            OUTPUT_DIR="$2"; shift 2 ;;
        *)
            echo "Unknown argument: $1" >&2; exit 1 ;;
    esac
done

# -- Resolve version ----------------------------------------------------------
if [[ -z "${VERSION}" ]]; then
    VERSION=$(grep -oP '(?<=<Version>)[^<]+' "${CSPROJ}" | head -1)
    if [[ -z "${VERSION}" ]]; then
        echo "Could not read <Version> from ${CSPROJ}." >&2
        exit 1
    fi
fi

echo "==> Building Adagio Machine Agent .deb v${VERSION}"

# -- Publish ------------------------------------------------------------------
PUBLISH_DIR=$(mktemp -d)
trap 'rm -rf "${PUBLISH_DIR}"' EXIT

echo "==> Publishing (linux-x64, self-contained)..."
dotnet publish "${CSPROJ}" \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    --output "${PUBLISH_DIR}/opt/adagio-machine-agent" \
    --nologo \
    2>&1 | grep -v "^Build succeeded\." || true

chmod 0755 "${PUBLISH_DIR}/opt/adagio-machine-agent/AdagioMachineAgent"

# -- Assemble package tree ----------------------------------------------------
PKG_DIR=$(mktemp -d)
trap 'rm -rf "${PUBLISH_DIR}" "${PKG_DIR}"' EXIT

INSTALL_ROOT="${PKG_DIR}/opt/adagio-machine-agent"
mkdir -p "${INSTALL_ROOT}"
cp -r "${PUBLISH_DIR}/opt/adagio-machine-agent/." "${INSTALL_ROOT}/"
chmod 0755 "${INSTALL_ROOT}/AdagioMachineAgent"

# systemd service unit
SERVICE_DST="${PKG_DIR}/lib/systemd/system"
mkdir -p "${SERVICE_DST}"
cp "${LINUX_INSTALLER_DIR}/adagio-agent.service" "${SERVICE_DST}/adagio-agent.service"

# installer scripts (referenced at runtime, not package scripts)
EXTRAS_DST="${PKG_DIR}/usr/share/adagio-machine-agent"
mkdir -p "${EXTRAS_DST}"
cp "${LINUX_INSTALLER_DIR}/install.sh"   "${EXTRAS_DST}/"
cp "${LINUX_INSTALLER_DIR}/uninstall.sh" "${EXTRAS_DST}/"
chmod 0755 "${EXTRAS_DST}/install.sh" "${EXTRAS_DST}/uninstall.sh"

# DEBIAN/ control files ---------------------------------------------------
DEBIAN_DIR="${PKG_DIR}/DEBIAN"
mkdir -p "${DEBIAN_DIR}"

cat > "${DEBIAN_DIR}/control" <<CONTROL
Package: adagio-machine-agent
Version: ${VERSION}
Section: utils
Priority: optional
Architecture: amd64
Depends: at-spi2-core, libdbus-1-3
Recommends: openssl
Maintainer: AdagioAgent Contributors
Description: Adagio Machine Agent
 REST-over-HTTPS agent that exposes process management and AT-SPI2 UI
 automation to GitHub Copilot via a VS Code controller extension.
 .
 Allows Copilot to launch, monitor, and interact with native GUI applications
 running on the host machine.
CONTROL

cat > "${DEBIAN_DIR}/postinst" <<'POSTINST'
#!/usr/bin/env bash
set -e
# Reload systemd only; the operator must run install.sh to configure secrets
# and start the service with the correct D-Bus session UID.
systemctl daemon-reload || true
echo ""
echo "Adagio Machine Agent binaries installed."
echo "Run /usr/share/adagio-machine-agent/install.sh as root to complete setup."
POSTINST
chmod 0755 "${DEBIAN_DIR}/postinst"

cat > "${DEBIAN_DIR}/prerm" <<'PRERM'
#!/usr/bin/env bash
set -e
if systemctl is-active --quiet adagio-agent 2>/dev/null; then
    systemctl stop adagio-agent || true
fi
if systemctl is-enabled --quiet adagio-agent 2>/dev/null; then
    systemctl disable adagio-agent || true
fi
PRERM
chmod 0755 "${DEBIAN_DIR}/prerm"

cat > "${DEBIAN_DIR}/postrm" <<'POSTRM'
#!/usr/bin/env bash
set -e
systemctl daemon-reload || true
POSTRM
chmod 0755 "${DEBIAN_DIR}/postrm"

# -- Build .deb ---------------------------------------------------------------
mkdir -p "${OUTPUT_DIR}"
DEB_PATH="${OUTPUT_DIR}/adagio-machine-agent_${VERSION}_amd64.deb"

echo "==> Building .deb..."
dpkg-deb --build "${PKG_DIR}" "${DEB_PATH}"

echo ""
echo "==> Package written: ${DEB_PATH}"
echo "    Install:   sudo dpkg -i ${DEB_PATH}"
echo "    Configure: sudo /usr/share/adagio-machine-agent/install.sh"
