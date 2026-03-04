param backendAppName string
param swaAppName string

resource backend 'Microsoft.App/containerApps@2024-03-01' existing = {
  name: backendAppName
}

resource swa 'Microsoft.Web/staticSites@2024-04-01' existing = {
  name: swaAppName
}

resource customBackend 'Microsoft.Web/staticSites/linkedBackends@2024-04-01' = {
  name: 'api'
  parent: swa
  properties: {
    backendResourceId: backend.id
    region: backend.location
  }
}
