@description('Container Apps Environment name')
param envName string

@description('Container App name')
param appName string

@description('Azure region')
param location string

@description('Log Analytics workspace resource ID')
param logAnalyticsWorkspaceId string

@description('Log Analytics shared key')
@secure()
param logAnalyticsSharedKey string

@description('Application Insights connection string')
param appInsightsConnectionString string

@description('Service Bus namespace name')
param serviceBusNamespace string

@description('Container image')
param containerImage string

@description('ghcr.io username')
@secure()
param ghcrUsername string

@description('ghcr.io PAT token')
@secure()
param ghcrPassword string

@description('Key Vault name')
param keyVaultName string

@description('User-assigned Managed Identity resource ID')
param managedIdentityId string

@description('User-assigned Managed Identity client ID')
param managedIdentityClientId string

@description('Resource ID of an existing Container Apps Environment to reuse (prod reuses dev env)')
param existingManagedEnvironmentId string = ''

var kvUri = 'https://${keyVaultName}${az.environment().suffixes.keyvaultDns}/'

// Only created for dev; prod passes existingManagedEnvironmentId instead
resource newEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = if (empty(existingManagedEnvironmentId)) {
  name: envName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: reference(logAnalyticsWorkspaceId, '2023-09-01').customerId
        sharedKey: logAnalyticsSharedKey
      }
    }
  }
}

var resolvedEnvId = empty(existingManagedEnvironmentId) ? newEnvironment.id : existingManagedEnvironmentId

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: appName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: resolvedEnvId
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        allowInsecure: false
      }
      registries: (empty(ghcrUsername) || empty(ghcrPassword)) ? [] : [
        {
          server: 'ghcr.io'
          username: ghcrUsername
          passwordSecretRef: 'ghcr-password'
        }
      ]
      secrets: concat(
        empty(ghcrPassword) ? [] : [
          {
            name: 'ghcr-password'
            value: ghcrPassword
          }
        ],
        [
          {
            name: 'sb-connection-string'
            keyVaultUrl: '${kvUri}secrets/ServiceBusConnectionString'
            identity: managedIdentityId
          }
        ]
      )
    }
    template: {
      containers: [
        {
          name: 'workapi'
          image: containerImage
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: appInsightsConnectionString
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: managedIdentityClientId
            }
            {
              name: 'ServiceBus__Namespace'
              value: '${serviceBusNamespace}.servicebus.windows.net'
            }
            {
              name: 'ServiceBus__ConnectionString'
              secretRef: 'sb-connection-string'
            }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: 8080
              }
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: 8080
              }
              periodSeconds: 15
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 2
      }
    }
  }
}

output fqdn string = containerApp.properties.configuration.ingress.fqdn
