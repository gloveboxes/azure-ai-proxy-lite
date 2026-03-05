param name string
param location string = resourceGroup().location
param tags object = {}

@description('The name of the user assigned identity that the container app will used to connect to the container registraty')
param identityName string
param containerAppsEnvironmentName string
param containerRegistryName string
param serviceName string = 'proxy'
param exists bool
@secure()
param storageConnectionString string
@secure()
param encryptionKey string
@secure()
param appInsightsConnectionString string
@secure()
param adminPassword string
param playgroundUrl string
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
    containerMaxReplicas: 1
    secrets: [
      {
        name: 'encryption-key'
        value: encryptionKey
      }
      {
        name: 'storage-connection-string'
        value: storageConnectionString
      }
      {
        name: 'app-insights-connection-string'
        value: appInsightsConnectionString
      }
      {
        name: 'admin-username'
        value: 'admin'
      }
      {
        name: 'admin-password'
        value: adminPassword
      }
    ]
    env: [
      {
        name: 'EncryptionKey'
        secretRef: 'encryption-key'
      }
      {
        name: 'ConnectionStrings__StorageAccount'
        secretRef: 'storage-connection-string'
      }
      {
        name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
        secretRef: 'app-insights-connection-string'
      }
      {
        name: 'Admin__Username'
        secretRef: 'admin-username'
      }
      {
        name: 'Admin__Password'
        secretRef: 'admin-password'
      }
      {
        name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED'
        value: 'true'
      }
      {
        name: 'PlaygroundUrl'
        value: playgroundUrl
      }
    ]
  }
}

output SERVICE_PROXY_IDENTITY_PRINCIPAL_ID string = proxyIdentity.properties.principalId
output SERVICE_PROXY_NAME string = app.outputs.name
output SERVICE_PROXY_URI string = app.outputs.uri
output SERVICE_PROXY_IMAGE_NAME string = app.outputs.imageName
