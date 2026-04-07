param name string
param location string = resourceGroup().location
param tags object = {}

@description('Principal ID of the proxy user-assigned managed identity for ACR pull')
param proxyPrincipalId string

@description('Principal ID of the proxy system-assigned managed identity used by DefaultAzureCredential')
param proxySystemPrincipalId string

// AI Foundry account
resource aiServices 'Microsoft.CognitiveServices/accounts@2025-04-01-preview' = {
  name: '${name}-aifoundry'
  location: location
  tags: tags
  kind: 'AIServices'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    customSubDomainName: '${name}-aifoundry'
    publicNetworkAccess: 'Enabled'
    allowProjectManagement: true
  }
  sku: {
    name: 'S0'
  }
}

// AI Foundry Project (child of AI Services — visible in ai.azure.com)
resource aiProject 'Microsoft.CognitiveServices/accounts/projects@2025-04-01-preview' = {
  parent: aiServices
  name: '${name}-aifoundry-project'
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {}
}

output aiServicesName string = aiServices.name
output aiServicesEndpoint string = aiServices.properties.endpoint
output aiProjectName string = aiProject.name

// Grant the proxy's user-assigned managed identity "Cognitive Services OpenAI User" on the AI Services account
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

// Grant the proxy's system-assigned managed identity "Cognitive Services OpenAI User" on the AI Services account
// DefaultAzureCredential in the proxy code uses the system-assigned identity
resource proxySystemOpenAIRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(aiServices.id, proxySystemPrincipalId, '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd')
  scope: aiServices
  properties: {
    principalId: proxySystemPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd')
    principalType: 'ServicePrincipal'
  }
}
