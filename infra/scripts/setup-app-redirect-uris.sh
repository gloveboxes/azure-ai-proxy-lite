#!/bin/bash

account_info=$(az account show 2>&1)
if [[ $? -ne 0 ]]; then
    echo "You must be logged in to Azure to run this script"
    echo "Run 'az login' to log in to Azure"
    exit 1
fi

echo "Loading azd .env file from current environment"

# Use the `get-values` azd command to retrieve environment variables from the `.env` file
while IFS='=' read -r key value; do
    value=$(echo "$value" | sed 's/^"//' | sed 's/"$//')
    export "$key=$value"
done <<EOF
$(azd env get-values)
EOF

# Use entraClientId (new) or AUTH_CLIENT_ID (legacy) variable name
CLIENT_ID="${entraClientId:-$AUTH_CLIENT_ID}"

if [ -z "$CLIENT_ID" ]; then
    echo "No Entra app registration configured (entraClientId not set). Skipping redirect URI setup."
    exit 0
fi

signin_path='/signin-oidc'

echo "Ensuring redirect URIs for app registration $CLIENT_ID"

app_registration=$(az ad app show --id $CLIENT_ID -o json)
existing_redirects=$(echo $app_registration | jq '.web.redirectUris')

if [ -n "$SERVICE_ADMIN_URI" ]; then
    echo "Checking redirect URI for $SERVICE_ADMIN_URI"
    if $(echo $existing_redirects | jq "contains([\"${SERVICE_ADMIN_URI}${signin_path}\"])"); then
        echo "  $SERVICE_ADMIN_URI$signin_path already registered"
    else
        echo "  Registering $SERVICE_ADMIN_URI$signin_path"
        az ad app update --id $CLIENT_ID --web-redirect-uris $(echo $existing_redirects | jq -r 'join(" ")') "${SERVICE_ADMIN_URI}${signin_path}"
    fi
else
    echo "SERVICE_ADMIN_URI not set — skipping redirect URI setup"
fi

echo "Redirect URI setup complete"

# Ensure the admin container app has the AzureAd env vars set.
# azd deploy creates new revisions that may not carry forward bicep-provisioned env vars,
# so we set them directly on the container app to ensure they persist.
if [ -n "$SERVICE_ADMIN_NAME" ] && [ -n "$CLIENT_ID" ]; then
    TENANT_ID="${entraTenantId:-$AUTH_TENANT_ID}"
    ADMIN_RG="${AZURE_ENV_NAME}-rg"

    echo "Ensuring AzureAd env vars on admin container app $SERVICE_ADMIN_NAME"
    az containerapp update \
        -n "$SERVICE_ADMIN_NAME" \
        -g "$ADMIN_RG" \
        --set-env-vars \
            "AzureAd__Instance=https://login.microsoftonline.com/" \
            "AzureAd__TenantId=$TENANT_ID" \
            "AzureAd__ClientId=$CLIENT_ID" \
            "AzureAd__CallbackPath=/signin-oidc" \
        --output none 2>&1
    echo "Admin container app env vars updated"
fi
