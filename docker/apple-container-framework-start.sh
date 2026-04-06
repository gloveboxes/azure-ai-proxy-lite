#!/bin/bash

# --- 1. System Check ---
container system status >/dev/null 2>&1 || container system start

# --- 2. Load Env ---
[ -f .env ] && export $(grep -v '^#' .env | xargs)

# --- 3. Cleanup ---
container rm -f azurite proxy registration >/dev/null 2>&1

# --- 4. Start Azurite ---
echo "🚀 Starting Azurite..."
container run -d \
  --name azurite \
  -p 10002:10002 \
  mcr.microsoft.com/azure-storage/azurite \
  azurite-table -l /data --tableHost 0.0.0.0

# In Apple's framework, 192.168.64.1 usually points back to the host
HOST_GATEWAY="192.168.64.1"

# --- 5. Start Proxy ---
echo "🚀 Starting Proxy..."
container run -d \
  --name proxy \
  -p ${PROXY_PORT:-8900}:8080 \
  -v dataprotection-keys:/home/app/.aspnet/DataProtection-Keys \
  --user root \
  --entrypoint /bin/sh \
  -e ASPNETCORE_URLS="http://+:8080" \
  -e ASPNETCORE_ENVIRONMENT="Production" \
  -e ConnectionStrings__StorageAccount="DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;TableEndpoint=http://${HOST_GATEWAY}:${AZURITE_TABLE_PORT:-10002}/devstoreaccount1;" \
  -e EncryptionKey="${ENCRYPTION_KEY:-change-this-encryption-key-in-production}" \
  -e Admin__Username="${ADMIN_USERNAME:-admin}" \
  -e Admin__Password="${ADMIN_PASSWORD:-admin}" \
  -e UseMockProxy="${USE_MOCK_PROXY:-false}" \
  -e RegistrationUrl="https://${REGISTRATION_HOST:-localhost}:${REGISTRATION_TLS_PORT:-4443}" \
  -e ProxyUrl="http://${REGISTRATION_HOST:-localhost}:${PROXY_PORT:-8900}/api/v1" \
  ${PROXY_IMAGE} \
  -c "chown 1654:1654 /home/app/.aspnet/DataProtection-Keys && exec runuser -u app -- ./AzureAIProxy"

# --- 6. Start Registration ---
echo "🚀 Starting Registration..."
container run -d \
  --name registration \
  -p ${REGISTRATION_PORT:-4280}:80 \
  -p ${REGISTRATION_TLS_PORT:-4443}:443 \
  -e PROXY_UPSTREAM="http://${HOST_GATEWAY}:${PROXY_PORT:-8900}" \
  ${REGISTRATION_IMAGE}

echo "⏳ Waiting for .NET startup..."
sleep 8
echo "✨ Try now: http://localhost:8900"
