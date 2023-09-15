using Cdk.AppService;
using Cdk.Core;
using Cdk.KeyVault;
using Cdk.ResourceManager;
using Cdk.Resources;
using Cdk.Sql;

// Defaults coming from env:
// AZURE_ENV_NAME
// AZURE_LOCATION
// AZURE_SUBSCRIPTION_ID
// AZURE_PRINCIPAL_ID
// AZURE_TENANT_ID

// New ResourceGroup for infra
var resourceGroup = new ResourceGroup();

// ** KeyVault
var keyVault = new KeyVault(resourceGroup, "kv");

// ** Secret for SQL - Password
var sqlAdminPasswordParam = new Parameter("sqlAdminPassword", "SQL Server administrator password", isSecure: true);
var sqlAdminSecret = new KeyVaultSecret(keyVault, "sqlAdminPassword");
sqlAdminSecret.AssignParameter(nameof(sqlAdminSecret.Properties.Properties.Value), sqlAdminPasswordParam);

var dbUserPasswordParam = new Parameter("dbUserPassword", "Database password", isSecure: true);
var dbPasswordSecret = new KeyVaultSecret(keyVault, "databasePassword");
dbPasswordSecret.AssignParameter(nameof(dbPasswordSecret.Properties.Properties.Value), dbUserPasswordParam);

// SQL Server
var sqlServer = new SqlServer(resourceGroup, "sql");
sqlServer.AssignParameter(nameof(sqlServer.Properties.AdministratorLoginPassword), sqlAdminPasswordParam);

// SQL Server config - Database / Firewall / deployment
var sqlDb = new SqlDatabase(sqlServer, "Todo");
var connString = new KeyVaultSecret(keyVault, "connectionString", sqlDb.GetConnectionString(dbUserPasswordParam));
new SqlFirewallRule(sqlServer, "sqlRules");
new DeploymentScript(resourceGroup, "cliScript", sqlDb, dbUserPasswordParam, sqlAdminPasswordParam);

// ** Service plan
var appServicePlan = new AppServicePlan(resourceGroup, "appServicePlan");

// api - api
var apiApp = new WebSite(resourceGroup, "api", appServicePlan, Runtime.Dotnetcore, "6.0");
apiApp.Properties.Tags.Add("azd-service-name","api");
// api app needs to know the name of the secret where the db - connection string is
apiApp.AddApplicationSetting("AZURE_SQL_CONNECTION_STRING_KEY", connString.Outputs.First(o=> o.Name.Contains("NAME")).Value);
apiApp.AddApplicationSetting("AZURE_KEY_VAULT_ENDPOINT", $"https://{keyVault.Outputs.First(o => o.Name.Contains($"{keyVault.Name}_NAME")).Value}.vault.azure.net/");
var apiPrincipalId = apiApp.AddOutput("SERVICE_API_IDENTITY_PRINCIPAL_ID", nameof(apiApp.Properties.Identity.PrincipalId), isSecure: true);
// Give api access to keyVault
keyVault.AddAccessPolicy(apiPrincipalId);
new WebSiteConfigLogs(apiApp, "apiLogs");

// web - app
var webApp = new WebSite(resourceGroup, "web", appServicePlan, Runtime.Node, "18-lts");
webApp.Properties.Tags.Add("azd-service-name","web");
// Set the apiUrl for the web
webApp.AddApplicationSetting("REACT_APP_API_BASE_URL", $"https://{apiApp.Outputs.First(o=>o.Name.Contains($"{apiApp.Name}_NAME")).Value}.azurewebsites.net");
new WebSiteConfigLogs(webApp, "webLogs");

// Handle output
string path = args.Length > 0 ? args[0] : "./infra";
new Infra().ToBicep(path);


class Infra : Infrastructure {
}
