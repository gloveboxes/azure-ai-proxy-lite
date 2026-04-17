param name string
param location string = resourceGroup().location
param tags object = {}

@description('The name of the user assigned identity that the container app will used to connect to the container registraty')
param identityName string
param containerAppsEnvironmentName string
param containerRegistryName string
param serviceName string = 'proxy'
param exists bool
param storageAccountName string
@secure()
param encryptionKey string
@secure()
param appInsightsConnectionString string
param registrationUrl string
param defaultApiVersion string = '2025-01-01-preview'
param imageName string = ''
param keyVaultName string = ''

resource proxyIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
}

module app '../core/host/container-app-upsert.bicep' = {
  name: '${serviceName}-container-app-module'
  params: {
    name: name
    location: location
    tags: union(tags, { 'azd-service-name': serviceName })
    identityName: proxyIdentity.name
    exists: exists
    containerAppsEnvironmentName: containerAppsEnvironmentName
    containerRegistryName: containerRegistryName
    targetPort: 8080
    containerCpuCoreCount: '0.75'
    containerMemory: '1.5Gi'
    containerMinReplicas: 0
    containerMaxReplicas: 1
    scaleCooldownPeriod: 432000
    scaleRules: [
      {
        name: 'http-requests'
        http: {
          metadata: {
            concurrentRequests: '10'
          }
        }
      }
    ]
    secrets: [
      {
        name: 'encryption-key'
        value: encryptionKey
      }
      {
        name: 'app-insights-connection-string'
        value: appInsightsConnectionString
      }
    ]
    env: [
      {
        name: 'EncryptionKey'
        secretRef: 'encryption-key'
      }
      {
        // Explicitly select the user-assigned identity for DefaultAzureCredential.
        name: 'AZURE_CLIENT_ID'
        value: proxyIdentity.properties.clientId
      }
      {
        name: 'StorageAccountName'
        value: storageAccountName
      }
      {
        name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
        secretRef: 'app-insights-connection-string'
      }
      {
        name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED'
        value: 'true'
      }
      {
        name: 'RegistrationUrl'
        value: registrationUrl
      }
      {
        name: 'DefaultApiVersion'
        value: defaultApiVersion
      }
    ]
  }
}

output SERVICE_PROXY_IDENTITY_PRINCIPAL_ID string = proxyIdentity.properties.principalId
output SERVICE_PROXY_SYSTEM_IDENTITY_PRINCIPAL_ID string = app.outputs.systemAssignedIdentityPrincipalId
output SERVICE_PROXY_NAME string = app.outputs.name
output SERVICE_PROXY_URI string = app.outputs.uri
output SERVICE_PROXY_IMAGE_NAME string = app.outputs.imageName
