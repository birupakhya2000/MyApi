targetScope = 'resourceGroup'

@description('Prefix for the prod app name; staging gets "-staging". Must be globally unique, e.g. myapi-iac-birupakhya')
param namePrefix string

param location string = resourceGroup().location

@description('Your Entra sign-in name (UPN). Becomes the SQL Entra admin')
param sqlAdminLogin string

@description('Object ID of that user')
param sqlAdminObjectId string

param tenantId string = subscription().tenantId

var suffix = uniqueString(resourceGroup().id)
var vaultName = take('kv-${replace(namePrefix, '-', '')}-${suffix}', 24)
var sqlServerName = toLower('${namePrefix}-sql-${suffix}')

var apps = [
  { name: '${namePrefix}-staging', secretName: 'jwt-key-staging', dbName: 'myapidb-staging' }
  { name: namePrefix, secretName: 'jwt-key-prod', dbName: 'myapidb-prod' }
]

// ---------- Monitoring ----------
resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${namePrefix}-logs'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${namePrefix}-insights'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

// ---------- Key Vault (RBAC mode; secret values are added by hand, never in code) ----------
resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: vaultName
  location: location
  properties: {
    tenantId: tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    publicNetworkAccess: 'Enabled'
  }
}

// ---------- Azure SQL: Entra-only auth, free-offer databases ----------
resource sql 'Microsoft.Sql/servers@2024-05-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      login: sqlAdminLogin
      sid: sqlAdminObjectId
      tenantId: tenantId
      principalType: 'User'
      azureADOnlyAuthentication: true
    }
  }
}

resource allowAzure 'Microsoft.Sql/servers/firewallRules@2024-05-01-preview' = {
  parent: sql
  name: 'AllowAllAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource databases 'Microsoft.Sql/servers/databases@2024-05-01-preview' = [for a in apps: {
  parent: sql
  name: a.dbName
  location: location
  sku: { name: 'GP_S_Gen5_2' }
  properties: {
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
  }
}]

// ---------- App Service ----------
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${namePrefix}-plan'
  location: location
  kind: 'linux'
  sku: {
    name: 'F1'
    tier: 'Free'
  }
  properties: {
    reserved: true
  }
}

resource sites 'Microsoft.Web/sites@2023-12-01' = [for a in apps: {
  name: a.name
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: [
        { name: 'EnableApiDocs', value: 'true' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
        { name: 'Jwt__Key', value: '@Microsoft.KeyVault(VaultName=${vault.name};SecretName=${a.secretName})' }
        {
          name: 'ConnectionStrings__Default'
          value: 'Server=tcp:${sql.properties.fullyQualifiedDomainName},1433;Initial Catalog=${a.dbName};Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;'
        }
      ]
    }
  }
}]

// ---------- Each app's identity may read secrets from the vault ----------
resource secretReaders 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (a, i) in apps: {
  scope: vault
  name: guid(vault.id, a.name, 'key-vault-secrets-user')
  properties: {
    // Built-in role: Key Vault Secrets User
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalId: sites[i].identity.principalId
    principalType: 'ServicePrincipal'
  }
}]

output stagingUrl string = 'https://${sites[0].properties.defaultHostName}'
output productionUrl string = 'https://${sites[1].properties.defaultHostName}'
output keyVaultName string = vault.name
output sqlServerFqdn string = sql.properties.fullyQualifiedDomainName