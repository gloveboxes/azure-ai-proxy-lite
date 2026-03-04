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

// The SWA linking auto-enables Easy Auth with RedirectToLoginPage,
// which blocks direct browser access to the container app (HTTP 400).
// Override to AllowAnonymous so the admin UI and proxy API work via direct access.
// The x-ms-client-principal header is still set for requests coming through SWA.
resource authConfig 'Microsoft.App/containerApps/authConfigs@2024-03-01' = {
  name: 'current'
  parent: backend
  dependsOn: [customBackend]
  properties: {
    platform: {
      enabled: true
    }
    globalValidation: {
      unauthenticatedClientAction: 'AllowAnonymous'
      excludedPaths: [
        '/account/login'
        '/account/logout'
        '/_framework'
        '/_content'
        '/_blazor'
      ]
    }
    identityProviders: {
      azureStaticWebApps: {
        enabled: true
        registration: {
          clientId: swa.properties.defaultHostname
        }
      }
    }
  }
}
