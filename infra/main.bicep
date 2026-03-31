targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name which is used to generate a short unique hash for each resource')
param name string

@minLength(1)
@description('Primary location for all resources')
param location string

param proxyAppExists bool = false

@description('Location for the Registration app resource group')
@allowed(['centralus', 'eastus2', 'eastasia', 'westeurope', 'westus2'])
@metadata({
  azd: {
    type: 'location'
  }
})
param swaLocation string

var adminUsername = 'admin'
// Generate a deterministic but unique admin password per deployment
var adminPassword = '${uniqueString(subscription().id, name, 'admin-pwd')}${uniqueString(name, location, 'admin-pwd')}'
// Generate a deterministic encryption key unique to this deployment
var encryptionKey = '${uniqueString(subscription().id, name, 'enc-key')}${uniqueString(name, location, 'enc-key')}${uniqueString(subscription().id, location, 'enc-key')}'

var resourceToken = toLower(uniqueString(subscription().id, name, location))
var tags = { 'azd-env-name': name }
var prefix = '${name}-${resourceToken}'

resource resourceGroup 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: '${name}-rg'
  location: location
  tags: tags
}

// Container apps host (including container registry)
module containerApps 'core/host/container-apps.bicep' = {
  name: 'container-apps'
  scope: resourceGroup
  params: {
    name: '${prefix}-app'
    location: location
    tags: tags
    containerAppsEnvironmentName: '${prefix}-cae'
    containerRegistryName: '${replace(prefix, '-', '')}registry'
    logAnalyticsWorkspaceName: monitoring.outputs.logAnalyticsWorkspaceName
  }
}

// Proxy app (combined proxy + admin)
module proxy 'app/proxy.bicep' = {
  name: 'proxy'
  scope: resourceGroup
  params: {
    name: '${prefix}-proxy'
    location: location
    tags: tags
    identityName: '${prefix}-id-proxy'
    containerAppsEnvironmentName: containerApps.outputs.environmentName
    containerRegistryName: containerApps.outputs.registryName
    exists: proxyAppExists
    storageConnectionString: storageAccount.outputs.connectionString
    encryptionKey: encryptionKey
    adminPassword: adminPassword
    registrationUrl: registration.outputs.SERVICE_WEB_URI
    appInsightsConnectionString: monitoring.outputs.applicationInsightsConnectionString
  }
}

// Azure Storage Account (Table Storage for all data)
module storageAccount 'storage.bicep' = {
  name: 'storage-account'
  scope: resourceGroup
  params: {
    name: take('${replace(prefix, '-', '')}st', 24)
    location: location
    tags: tags
  }
}

// The Registration frontend
module registration 'registration.bicep' = {
  name: 'registration'
  scope: resourceGroup
  params: {
    name: '${prefix}-registration'
    location: swaLocation
    tags: tags
  }
}

// link Registration to Proxy backend
module swaLinkDotnet './linkSwaResource.bicep' = {
  name: 'frontend-link-dotnet'
  scope: resourceGroup
  params: {
    swaAppName: registration.outputs.SERVICE_WEB_NAME
    backendAppName: proxy.outputs.SERVICE_PROXY_NAME
  }
}

// Monitor application with Azure Monitor
module monitoring 'core/monitor/monitoring.bicep' = {
  name: 'monitoring'
  scope: resourceGroup
  params: {
    location: location
    tags: tags
    applicationInsightsDashboardName: '${prefix}-appinsights-dashboard'
    applicationInsightsName: '${prefix}-appinsights'
    logAnalyticsName: '${take(prefix, 50)}-loganalytics' // Max 63 chars
  }
}

output AZURE_LOCATION string = location
output AZURE_CONTAINER_ENVIRONMENT_NAME string = containerApps.outputs.environmentName
output AZURE_CONTAINER_REGISTRY_NAME string = containerApps.outputs.registryName
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = containerApps.outputs.registryLoginServer

output SERVICE_PROXY_IDENTITY_PRINCIPAL_ID string = proxy.outputs.SERVICE_PROXY_IDENTITY_PRINCIPAL_ID
output SERVICE_PROXY_NAME string = proxy.outputs.SERVICE_PROXY_NAME
output SERVICE_PROXY_URI string = proxy.outputs.SERVICE_PROXY_URI
output SERVICE_PROXY_IMAGE_NAME string = proxy.outputs.SERVICE_PROXY_IMAGE_NAME

output SERVICE_REGISTRATION_URI string = registration.outputs.SERVICE_WEB_URI

output SERVICE_STORAGE_ACCOUNT_NAME string = storageAccount.outputs.name

output SERVICE_ADMIN_USERNAME string = adminUsername
output SERVICE_ADMIN_PASSWORD string = adminPassword
#disable-next-line outputs-should-not-contain-secrets
output SERVICE_ENCRYPTION_KEY string = encryptionKey
