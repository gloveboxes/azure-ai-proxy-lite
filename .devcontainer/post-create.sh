
echo Installing .NET 10.0 SDK...

# Detect architecture
ARCH=$(uname -m)
if [ "$ARCH" = "x86_64" ]; then
    DOTNET_ARCH="x64"
elif [ "$ARCH" = "aarch64" ] || [ "$ARCH" = "arm64" ]; then
    DOTNET_ARCH="arm64"
else
    echo "Unsupported architecture: $ARCH"
    exit 1
fi

# Download and install .NET 10.0 SDK
DOTNET_VERSION="10.0.100"
DOTNET_URL="https://dotnetcli.azureedge.net/dotnet/Sdk/${DOTNET_VERSION}/dotnet-sdk-${DOTNET_VERSION}-linux-${DOTNET_ARCH}.tar.gz"

echo "Downloading .NET ${DOTNET_VERSION} SDK for ${DOTNET_ARCH}..."
cd /tmp
curl -sSL "${DOTNET_URL}" -o dotnet-sdk-10.tar.gz

echo "Installing .NET 10.0 SDK..."
sudo tar -xzf dotnet-sdk-10.tar.gz -C /usr/share/dotnet
rm dotnet-sdk-10.tar.gz

# Verify installation
dotnet --list-sdks

echo Setting up Python environment...

pip3 install --user -r requirements-dev.txt

echo Setting up commit hooks...
pre-commit install --install-hooks

echo Setting up Playground environment...
cd src/playground
. ${NVM_DIR}/nvm.sh
nvm install
npm i
npm install -g @azure/static-web-apps-cli

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
