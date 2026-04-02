# Authenticating azd with a Specific Tenant and Subscription

## Prerequisites

- [Azure Developer CLI (azd)](https://learn.microsoft.com/en-us/azure/developer/azure-developer-cli/install-azd) installed
- [Azure CLI (az)](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli) installed
- An Azure subscription and its tenant ID

## Step 1: Log in to azd with your tenant

```bash
azd auth login --tenant-id <YOUR_TENANT_ID>
```

> **Note:** If your subscription is in a different tenant from your default, you must specify `--tenant-id` so `azd` can access it.

## Step 2: Create a new azd environment

```bash
azd env new <ENVIRONMENT_NAME>
```

## Step 3: Set the subscription ID in the azd environment

```bash
azd env set AZURE_SUBSCRIPTION_ID <YOUR_SUBSCRIPTION_ID>
```

## Step 4: Set the default subscription (optional)

```bash
azd config set defaults.subscription <YOUR_SUBSCRIPTION_ID>
```

## Step 5: Deploy

```bash
azd up
```

You will be prompted to select a location/region. The subscription prompt will be skipped since it was set in Step 3.

## Troubleshooting

### "failed to resolve user access to subscription"

This means `azd` is not authenticated to the tenant that owns your subscription. Run:

```bash
azd auth login --tenant-id <YOUR_TENANT_ID>
```

### Subscription not visible in `az account list`

Your subscription may be in a different tenant. Log in with the specific tenant:

```bash
az login --tenant <YOUR_TENANT_ID>
```

### Finding your Tenant ID and Subscription ID

List all accessible subscriptions and their tenants:

```bash
az account list --query "[].{Name:name, SubscriptionId:id, TenantId:tenantId}" --output table
```

Or find them in the [Azure Portal](https://portal.azure.com) under **Subscriptions**.
