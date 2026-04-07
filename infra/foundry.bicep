param name string
param location string = resourceGroup().location
param tags object = {}

@description('Principal ID of the proxy managed identity to grant Cognitive Services OpenAI User access')
param proxyPrincipalId string

// AI Services account (multi-service resource for model deployments)
resource aiServices 'Microsoft.CognitiveServices/accounts@2024-04-01-preview' = {
  name: '${name}-aiservices'
  location: location
  tags: tags
  kind: 'AIServices'
  properties: {
    customSubDomainName: '${name}-aiservices'
    publicNetworkAccess: 'Enabled'
  }
  sku: {
    name: 'S0'
  }
}

// Key Vault for AI Hub workspace
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: take('${replace(name, '-', '')}aikv', 24)
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
  }
}

// Storage account for AI Hub workspace
resource aiStorage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: take('${replace(name, '-', '')}aist', 24)
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
  }
}

// AI Hub workspace
resource aiHub 'Microsoft.MachineLearningServices/workspaces@2024-10-01' = {
  name: '${name}-aihub'
  location: location
  tags: tags
  kind: 'Hub'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    friendlyName: '${name} AI Hub'
    storageAccount: aiStorage.id
    keyVault: keyVault.id
  }
}

// Connect AI Services to Hub
resource aiServicesConnection 'Microsoft.MachineLearningServices/workspaces/connections@2024-10-01' = {
  parent: aiHub
  name: 'aiservices'
  properties: {
    category: 'AIServices'
    target: aiServices.properties.endpoint
    authType: 'ApiKey'
    credentials: {
      key: aiServices.listKeys().key1
    }
    metadata: {
      ApiType: 'Azure'
      ResourceId: aiServices.id
    }
  }
}

// AI Foundry Project
resource aiProject 'Microsoft.MachineLearningServices/workspaces@2024-10-01' = {
  name: '${name}-aiproject'
  location: location
  tags: tags
  kind: 'Project'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    friendlyName: '${name} AI Project'
    hubResourceId: aiHub.id
  }
}

output aiServicesName string = aiServices.name
output aiServicesEndpoint string = aiServices.properties.endpoint
output aiProjectName string = aiProject.name
output aiServicesId string = aiServices.id

// Grant the proxy's managed identity "Cognitive Services OpenAI User" on the AI Services account
// Role definition ID: 5e0bd9bd-7b93-4f28-af87-19fc36ad61bd
resource proxyOpenAIRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(aiServices.id, proxyPrincipalId, '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd')
  scope: aiServices
  properties: {
    principalId: proxyPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd')
    principalType: 'ServicePrincipal'
  }
}
