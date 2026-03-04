
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

echo setting up postgresql...

# install postgresql client
sudo sh -c 'echo "deb http://apt.postgresql.org/pub/repos/apt $(lsb_release -cs)-pgdg main" > /etc/apt/sources.list.d/pgdg.list'
wget --quiet -O - https://www.postgresql.org/media/keys/ACCC4CF8.asc | gpg --dearmor | sudo tee /etc/apt/trusted.gpg.d/postgresql.gpg >/dev/null

sudo apt-get update
sudo apt install postgresql-client -y

psql -U admin -d aoai-proxy -h localhost -w -c 'CREATE ROLE azure_pg_admin WITH NOLOGIN NOSUPERUSER INHERIT NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;'

psql -U admin -d aoai-proxy -h localhost -w -c 'CREATE ROLE aoai_proxy_app WITH NOLOGIN NOSUPERUSER INHERIT NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;'

psql -U admin -d aoai-proxy -h localhost -w -c 'CREATE ROLE aoai_proxy_reporting WITH NOLOGIN NOSUPERUSER INHERIT NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;'

psql -U admin -d aoai-proxy -h localhost -w -c 'GRANT aoai_proxy_app TO azure_pg_admin;'
psql -U admin -d aoai-proxy -h localhost -w -c 'GRANT aoai_proxy_reporting TO azure_pg_admin;'


psql -U admin -d aoai-proxy -h localhost -w -f ./database/aoai-proxy.sql

echo Setting up Python environment...

python3 -m pip install -r requirements-dev.txt

echo Setting up commit hooks...
pre-commit install

echo Setting up Playground environment...
cd src/playground
. ${NVM_DIR}/nvm.sh
nvm install
npm i
npm install -g @azure/static-web-apps-cli
