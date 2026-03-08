# Managed Identity Support for Azure OpenAI Service

## Overview
This implementation adds support for Azure Managed Identity authentication to access Azure OpenAI Service models, alongside the existing API key authentication method.

## Changes Summary

### 1. **Data Model Updates**
Added `UseManagedIdentity` boolean flag to catalog entities:

- **`Deployment.cs`** - Added `UseManagedIdentity` property
- **`CatalogEntity.cs`** - Added `UseManagedIdentity` property (Table Storage entity)
- **`OwnerCatalog.cs`** - Added `UseManagedIdentity` property (Database model)
- **`ModelEditorModel.cs`** - Added `UseManagedIdentity` property (UI model)

### 2. **Service Layer Updates**

#### **CatalogService.cs**
- Updated `GetDecryptedEventCatalogAsync()` to include `UseManagedIdentity` flag
- Updated `GetEventAssistantAsync()` to include `UseManagedIdentity` flag

#### **ModelService.cs**
- Updated `AddOwnerCatalogAsync()` to persist `UseManagedIdentity` flag
- Updated `GetOwnerCatalogAsync()` to retrieve `UseManagedIdentity` flag
- Updated `DuplicateOwnerCatalogAsync()` to copy `UseManagedIdentity` flag
- Updated `GetOwnerCatalogsAsync()` to include `UseManagedIdentity` flag
- Updated `UpdateOwnerCatalogAsync()` to update `UseManagedIdentity` flag

#### **ProxyService.cs**
- Added `Azure.Core` and `Azure.Identity` using statements
- Added `DefaultAzureCredential` static instance
- **New method**: `GetAuthenticationHeaderAsync(Deployment, bool)` - Creates appropriate authentication header based on deployment configuration:
  - **Managed Identity**: Acquires token using `DefaultAzureCredential` for Cognitive Services scope
  - **API Key**: Uses traditional api-key or Bearer token format

#### **MockProxyService.cs**
- Implemented `GetAuthenticationHeaderAsync()` for mock/test scenarios

### 3. **Route Updates**
Updated all route handlers to use the new authentication method:

- **AzureOpenAI.cs** - Updated to call `GetAuthenticationHeaderAsync()`
- **AzureOpenAIAssistants.cs** - Updated to call `GetAuthenticationHeaderAsync()`
- **AzureOpenAIFiles.cs** - Updated to call `GetAuthenticationHeaderAsync()`
- **AzureInference.cs** - Updated to call `GetAuthenticationHeaderAsync()` with Bearer token format

### 4. **UI Updates**

#### **ModelEditor.razor**
- Added "Use Managed Identity" switch control
- Positioned between Region and Active fields

#### **ModelEdit.razor.cs**
- Updated to handle `UseManagedIdentity` property in model binding
- Persists managed identity flag when saving

### 5. **Seed Data**

#### **seed_data.py**
- Updated catalog creation to include `UseManagedIdentity: False` by default

## How It Works

### Authentication Flow

1. **Request arrives** at proxy endpoint
2. **Catalog lookup** retrieves deployment configuration including `UseManagedIdentity` flag
3. **Authentication header creation**:
   - If `UseManagedIdentity = true`:
     - Uses `DefaultAzureCredential` to acquire token
     - Token scope: `https://cognitiveservices.azure.com/.default`
     - Header: `Authorization: Bearer <token>`
   - If `UseManagedIdentity = false`:
     - Uses encrypted API key from catalog
     - Header: `api-key: <key>` (or `Authorization: Bearer <key>` for Azure Inference)
4. **Request forwarded** to upstream Azure OpenAI endpoint with appropriate auth

### DefaultAzureCredential Chain
The proxy uses `DefaultAzureCredential` which tries authentication methods in this order:
1. Environment variables
2. Managed Identity (in Azure)
3. Visual Studio credentials
4. Azure CLI credentials
5. Azure PowerShell credentials

## Usage

### Setup Steps

1. **Deploy the proxy to Azure**
   ```bash
   azd up
   ```

2. **Grant proxy access to your Azure OpenAI service**

   Get the proxy's managed identity principal ID:
   ```bash
   PROXY_IDENTITY=$(azd env get-value SERVICE_PROXY_IDENTITY_PRINCIPAL_ID)
   ```

   Grant the "Cognitive Services OpenAI User" role:
   ```bash
   az role assignment create \
     --assignee $PROXY_IDENTITY \
     --role "Cognitive Services OpenAI User" \
     --scope /subscriptions/<sub-id>/resourceGroups/<rg>/providers/Microsoft.CognitiveServices/accounts/<openai-name>
   ```

3. **Add models using the Admin UI**
   - Navigate to the proxy admin UI
   - Go to "Models" → "Add Model"
   - Fill in the model details:
     - Endpoint URL of your Azure OpenAI service
     - Deployment name
     - **Check "Use Managed Identity"**
     - Leave Key field blank (not used with managed identity)
   - Save

4. **Use the proxy**
   - The proxy will authenticate to Azure OpenAI using managed identity
   - No API keys needed in the catalog

### Benefits

- **More Secure**: No API keys stored in the proxy
- **Flexible**: Grant access to multiple OpenAI services across regions
- **RBAC-based**: Use Azure role assignments for granular control
- **Works with existing services**: Connect to your already-deployed OpenAI resources

### For Administrators

#### Deploy with Sample OpenAI Service
```bash
azd up
# When prompted, set deploySampleOpenAI=true
```

Or via parameter:
```bash
azd deploy --parameter deploySampleOpenAI=true
```

#### Add Managed Identity Model via UI
1. Navigate to Models page
2. Click "Add Model"
3. Fill in:
   - **Friendly Name**: e.g., "Production GPT-4"
   - **Deployment Name**: Your Azure OpenAI deployment name
   - **Type**: Select model type
   - **Endpoint**: Your Azure OpenAI endpoint URL
   - **Key**: Leave blank or use placeholder (not used when MI is enabled)
   - **Region**: Azure region
   - **Use Managed Identity**: ✓ Check this box
   - **Active**: ✓ Check to enable
4. Save

#### Configure Existing Azure OpenAI Service
If using your own Azure OpenAI service (not the sample):

1. Grant the proxy's managed identity access to your Azure OpenAI service:
```bash
# Get the proxy identity
PROXY_IDENTITY=$(az containerapp show \
  --name <proxy-app-name> \
  --resource-group <resource-group> \
  --query identity.principalId -o tsv)

# Assign Cognitive Services OpenAI User role
az role assignment create \
  --assignee $PROXY_IDENTITY \
  --role "5e0bd9bd-7b93-4f28-af87-19fc36ad61bd" \
  --scope /subscriptions/<sub-id>/resourceGroups/<rg>/providers/Microsoft.CognitiveServices/accounts/<openai-account>
```

2. Add model in UI with Managed Identity enabled

### For Developers

#### Testing Locally
The proxy uses `DefaultAzureCredential` which will fall back to:
- Azure CLI credentials: Run `az login` first
- Environment variables: Set `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_CLIENT_SECRET`

## Benefits

1. **Enhanced Security**: No API keys stored or transmitted (when using MI)
2. **Simplified Management**: Azure handles token lifecycle
3. **Audit Trail**: Azure Monitor tracks all authentication events
4. **Flexibility**: Support both MI and API key auth per model
5. **Future-Proof**: Foundation for multiple managed identity services

## Backward Compatibility

✅ **Fully backward compatible**
- Existing API key-based models continue to work
- `UseManagedIdentity` defaults to `false`
- No breaking changes to existing deployments
- UI gracefully handles both authentication methods

## Testing

Build succeeded:
```bash
dotnet build src/AzureAIProxy.sln
# Build succeeded in 2.4s
```

## Next Steps

1. Test with actual Azure OpenAI service using managed identity
2. Verify token acquisition and caching
3. Monitor token refresh behavior
4. Consider adding token caching optimization
5. Document for end users
6. Add telemetry for authentication method used
