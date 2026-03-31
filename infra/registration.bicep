param name string
param location string = resourceGroup().location
param tags object = {}

param serviceName string = 'registration'

module registration 'core/host/staticwebapp.bicep' = {
  name: '${serviceName}-staticwebapp-module'
  params: {
    name: name
    location: location
    tags: union(tags, { 'azd-service-name': serviceName })
    sku: {
      name: 'Standard'
      size: 'Standard'
    }
  }
}

output SERVICE_WEB_NAME string = registration.outputs.name
output SERVICE_WEB_URI string = registration.outputs.uri
