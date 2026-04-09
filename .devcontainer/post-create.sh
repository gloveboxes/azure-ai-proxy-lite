#!/usr/bin/env bash
set -euo pipefail

echo Setting up Python environment...

pip3 install -r requirements-dev.txt

echo Setting up commit hooks...
pre-commit install --install-hooks

echo Setting up Registration environment...
cd src/registration
npm i
cd /workspaces/azure-ai-proxy-lite
npm install -g @azure/static-web-apps-cli

echo Setting up Playwright test environment...
if [ -d tests/playwright ]; then
    cd tests/playwright
    npm ci
    # Install Chromium plus required Linux runtime packages in one step.
    npx playwright install --with-deps chromium
    cd /workspaces/azure-ai-proxy-lite
else
    echo "tests/playwright not found, skipping Playwright setup"
fi

# On arm64 hosts, the SWA CLI's StaticSitesClient binary is x86-64 only.
# Install QEMU user-mode emulation + x86-64 libraries so it can run transparently.
if [ "$(uname -m)" = "aarch64" ] || [ "$(uname -m)" = "arm64" ]; then
    echo "arm64 detected — installing QEMU x86-64 emulation for SWA CLI..."
    sudo apt-get install -y qemu-user-static
    sudo dpkg --add-architecture amd64
    sudo apt-get update
    sudo apt-get install -y libc6:amd64 zlib1g:amd64 libstdc++6:amd64 libicu72:amd64 libssl3:amd64

    # Register binfmt handler so x86-64 ELF binaries run automatically via QEMU
    if [ -d /proc/sys/fs/binfmt_misc ]; then
        sudo mount -t binfmt_misc binfmt_misc /proc/sys/fs/binfmt_misc 2>/dev/null || true
        if [ -e /proc/sys/fs/binfmt_misc/register ]; then
            echo ':qemu-x86_64:M::\x7fELF\x02\x01\x01\x00\x00\x00\x00\x00\x00\x00\x00\x00\x02\x00\x3e\x00:\xff\xff\xff\xff\xff\xfe\xfe\x00\xff\xff\xff\xff\xff\xff\xff\xff\xfe\xff\xff\xff:/usr/bin/qemu-x86_64-static:OCF' \
                | sudo tee /proc/sys/fs/binfmt_misc/register >/dev/null 2>&1 || true
        fi
    fi
    echo "QEMU x86-64 emulation setup complete."
fi
