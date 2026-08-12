targetScope = 'resourceGroup'

@description('Environment name (dev or prod)')
@allowed(['dev', 'prod'])
param environment string = 'dev'

@description('Azure region')
param location string = resourceGroup().location

@description('Base name for all resources')
param baseName string = 'zeiss-work'

@description('Container image to deploy')
param containerImage string = 'ghcr.io/placeholder/workapi:latest'

@description('GitHub Container Registry username')
@secure()
param ghcrUsername string = ''

@description('GitHub Container Registry PAT')
@secure()
param ghcrPassword string = ''

var suffix = '${baseName}-${environment}'
var uniqueSuffix = uniqueString(resourceGroup().id, suffix)

// Log Analytics Workspace
module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring-${environment}'
  params: {
    name: 'log-${suffix}'
    appInsightsName: 'ai-${suffix}'
    location: location
  }
}

// Service Bus
module serviceBus 'modules/service-bus.bicep' = {
  name: 'servicebus-${environment}'
  params: {
    name: 'sb-${uniqueSuffix}'
    location: location
    queueName: 'work-queue'
  }
}

// Key Vault
module keyVault 'modules/key-vault.bicep' = {
  name: 'keyvault-${environment}'
  params: {
    name: 'kv-${uniqueSuffix}'
    location: location
    serviceBusConnectionString: serviceBus.outputs.connectionString
  }
}

// Container Apps
module containerApps 'modules/container-apps.bicep' = {
  name: 'containerapps-${environment}'
  params: {
    envName: 'cae-${suffix}'
    appName: 'ca-${suffix}'
    location: location
    logAnalyticsWorkspaceId: monitoring.outputs.workspaceId
    logAnalyticsSharedKey: monitoring.outputs.workspaceKey
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    serviceBusNamespace: serviceBus.outputs.namespaceName
    containerImage: containerImage
    ghcrUsername: ghcrUsername
    ghcrPassword: ghcrPassword
  }
}

// Grant Container App's Managed Identity access to Service Bus and Key Vault
module rbac 'modules/rbac.bicep' = {
  name: 'rbac-${environment}'
  params: {
    serviceBusNamespaceName: serviceBus.outputs.namespaceName
    keyVaultName: keyVault.outputs.name
    principalId: containerApps.outputs.identityPrincipalId
  }
}

output containerAppUrl string = containerApps.outputs.fqdn
output appInsightsName string = monitoring.outputs.appInsightsName
