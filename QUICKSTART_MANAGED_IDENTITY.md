# Quick Start Guide - Managed Identity Setup

## Using Managed Identity with Your Azure OpenAI Service

The proxy supports **Azure Managed Identity authentication** - no API keys needed!

### Prerequisites

- An existing Azure OpenAI Service (or deploy a new one)
- The proxy deployed to Azure Container Apps (via `azd up`)
- You are logged in to a tenant where you can create Entra ID app registrations (`az login --tenant <tenant-id>`)

### Setup Steps

#### 1. Deploy the proxy

```bash
azd auth login --tenant-id <your-tenant-id> --use-device-code
azd config set defaults.subscription <your-subscription-id>
```

The deployment hooks use the Azure CLI (`az`) to create the Entra ID app registration, so you must also log in with `az`:

```bash
az login --tenant <your-tenant-id> --use-device-code
az account set --subscription <your-subscription-id>
```

Then provision and deploy:

```bash
azd up
```

The deployment automatically creates an Entra ID app registration and configures Microsoft login for the admin UI.

#### 2. Managed identity is pre-configured for the deployed Foundry project

The deployment automatically grants the proxy's managed identity **Cognitive Services OpenAI User** access to the AI Services account created alongside the Foundry project. No manual `az role assignment create` is needed for models deployed into that project.

> **Using an external Azure OpenAI service?** You need to grant access manually — see [Granting access to additional Azure OpenAI services](#granting-access-to-additional-azure-openai-services) below.

#### 3. Add models to the proxy catalog

- Go to the admin UI: `<SERVICE_ADMIN_URI>` (from `azd env get-values`)
- Sign in with your Microsoft account
- Navigate to "Models" > "Add Model"
- Fill in the form:
  - **Friendly Name**: e.g., "GPT-4o-mini"
  - **Deployment Name**: Your model deployment name (e.g., `gpt-4o-mini`)
  - **Type**: `Foundry_Model`
  - **Endpoint**: Your Azure OpenAI endpoint (e.g., `https://my-openai.openai.azure.com`)
  - **Key**: Leave blank (not used with managed identity)
  - **Region**: Your region (e.g., `eastus`)
  - **Use Managed Identity**: ✓ **Check this box**
  - **Active**: ✓ Check to enable
- Click "Save"

#### 4. Create an event and start using the proxy

That's it! The proxy will now authenticate to Azure OpenAI using managed identity instead of API keys.

### Benefits

- **🔒 More secure** - No API keys to manage or rotate
- **🎯 Granular access** - Grant access per OpenAI service using RBAC
- **♻️ Works with existing services** - Use your already deployed OpenAI resources
- **🌍 Multi-region** - Connect to OpenAI services across regions
- **🔄 Easy to update** - Just add/remove role assignments as needed

### Granting access to additional Azure OpenAI services

To use managed identity with Azure OpenAI or AI Services accounts **outside** the deployed Foundry project, grant the proxy's managed identity access manually.

First, get the proxy's managed identity principal ID:

```bash
PROXY_IDENTITY=$(az containerapp show \
  --name <your-proxy-name> \
  --resource-group <your-resource-group> \
  --query identity.principalId -o tsv)
```

**Tip:** You can get resource names from the output of `azd env get-values`.

Then grant access to each service:

```bash
az role assignment create \
  --assignee $PROXY_IDENTITY \
  --role "Cognitive Services OpenAI User" \
  --scope /subscriptions/<subscription-id>/resourceGroups/<rg-name>/providers/Microsoft.CognitiveServices/accounts/<openai-name>
```

### Multiple OpenAI Services

You can grant the proxy access to multiple Azure OpenAI services across regions:

```bash
# Grant access to service in East US
az role assignment create \
  --assignee $PROXY_IDENTITY \
  --role "Cognitive Services OpenAI User" \
  --scope <openai-eastus-resource-id>

# Grant access to service in West Europe
az role assignment create \
  --assignee $PROXY_IDENTITY \
  --role "Cognitive Services OpenAI User" \
  --scope <openai-westeurope-resource-id>
```

Then add models from each service to your catalog with the "Use Managed Identity" toggle enabled.
