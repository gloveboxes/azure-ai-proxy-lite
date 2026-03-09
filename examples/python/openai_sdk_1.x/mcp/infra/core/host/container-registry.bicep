param name string
param location string = resourceGroup().location
param tags object = {}
param adminUserEnabled bool = true
param sku object = {
  name: 'Basic'
}

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: name
  location: location
  tags: tags
  sku: sku
  properties: {
    adminUserEnabled: adminUserEnabled
  }
}

output loginServer string = containerRegistry.properties.loginServer
output name string = containerRegistry.name
