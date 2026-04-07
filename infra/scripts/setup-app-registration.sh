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

AUTH_APP_NAME="$AZURE_ENV_NAME-app"

signin_path='/signin-oidc'

app_registrations=$(az ad app list --filter "displayname eq '$AUTH_APP_NAME'" -o json)
app_count=$(echo $app_registrations | jq '. | length')

if [ $app_count -eq 0 ]; then
    echo "Creating app registration for $AUTH_APP_NAME"
    app_id=$(az ad app create --display-name $AUTH_APP_NAME --sign-in-audience AzureADMyOrg --enable-id-token-issuance true | jq -r '.appId')
else
    echo "App registration for $AUTH_APP_NAME already exists"
    app_id=$(echo $app_registrations | jq -r '.[0].appId')

    # Ensure ID token issuance is enabled
    az ad app update --id $app_id --enable-id-token-issuance true 2>/dev/null
fi

tenantId=$(az account show | jq -r '.tenantId')

echo "Adding environment variables to azd environment"
azd env set entraClientId $app_id
azd env set entraTenantId $tenantId

echo "App Registration complete (clientId=$app_id, tenantId=$tenantId)"
