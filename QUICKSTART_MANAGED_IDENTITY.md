# Quick Start Guide - Managed Identity Setup

## Using Managed Identity with Your Azure OpenAI Service

The proxy now supports **Azure Managed Identity authentication** - no API keys needed! 🎉

### Prerequisites

- An existing Azure OpenAI Service (or deploy a new one)
- The proxy deployed to Azure Container Apps (via `azd up`)

### Setup Steps

#### 1. Deploy the proxy

```bash
azd up
```

#### 2. Grant the proxy access to your Azure OpenAI service

Get the proxy's managed identity:

```bash
PROXY_IDENTITY=$(az containerapp show \
  --name <your-proxy-name> \
  --resource-group <your-resource-group> \
  --query identity.principalId -o tsv)
```

Grant access to your Azure OpenAI service:

```bash
az role assignment create \
  --assignee $PROXY_IDENTITY \
  --role "Cognitive Services OpenAI User" \
  --scope /subscriptions/<subscription-id>/resourceGroups/<rg-name>/providers/Microsoft.CognitiveServices/accounts/<openai-name>
```

**Tip:** You can get resource details from the output of `azd env get-values`

#### 3. Add models to the proxy catalog

- Go to the admin UI: `<SERVICE_PROXY_URI>` (from `azd env get-values`)
- Login with username: `admin`, password: `<SERVICE_ADMIN_PASSWORD>`
- Navigate to "Models" → "Add Model"
- Fill in the form:
  - **Friendly Name**: e.g., "GPT-4o-mini"
  - **Deployment Name**: Your model deployment name (e.g., `gpt-4o-mini`)
  - **Type**: `OpenAI_Chat`
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

### Multiple OpenAI Services

You can grant the proxy access to multiple Azure OpenAI services:

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
