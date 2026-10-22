import { Construct } from "constructs";
import { App, AzurermBackend, TerraformStack } from "cdktf";
import { AzurermProvider } from "@cdktf/provider-azurerm/lib/provider";
import { AppConfiguration } from "@cdktf/provider-azurerm/lib/app-configuration";
import { DataAzurermContainerRegistry } from "@cdktf/provider-azurerm/lib/data-azurerm-container-registry";
import { AzapiProvider, DboteGateway, DboteInsights, Environnment } from "./core";
import { DboteIdentityProvider } from "./app/dbote-identity-provider";
import { DboteSampleApp } from "./app/dbote-sample-app";

// The reference deployment of this repository: the shared platform, the gateway from
// @dbosoft/bote-infra (./core) and the samples as the application on top of it. An application
// of its own builds the same stack with its own application layer instead of DboteSampleApp.

const location = "westeurope"; // Define the location for resources
const resourceGroupName = "rg-dbote-test"; // Define the resource group name
const tenantId = 'cb0bb315-f38b-4ab4-ad2d-d3ed25d23b53';
const subscriptionId = '998590b9-1c43-452a-9b02-dcf9ed5acb64';


class DboteStack extends TerraformStack {
  constructor(scope: Construct, id: string) {
    super(scope, id);

    new AzurermProvider(this, "azurerm", {
      tenantId,
      subscriptionId,
      resourceProviderRegistrations: 'none',
      features: [
        {
          appConfiguration: [{ purgeSoftDeleteOnDestroy: false, recoverSoftDeleted: true }],
        },
      ],
    });

    new AzapiProvider(this, "azapi");

    const environment = new Environnment(tenantId, location, resourceGroupName, 'cmdev');

    // ---- Platform: central, provided once per platform and handed to the gateway as inputs.
    // Created here only because this reference deployment has no platform of its own.

    const insights = new DboteInsights(this, environment);

    const appConfiguration = new AppConfiguration(this, environment.formatName('appcs'), {
      name: environment.formatName('appcs'),
      location: location,
      resourceGroupName: resourceGroupName,
      sku: 'developer',
    });

    // The registry already exists; the worker image is pushed there by Prepare-Artifacts.ps1.
    const registry = new DataAzurermContainerRegistry(this, 'acrdbotecmdev', {
      name: 'acrdbotecmdev',
      resourceGroupName,
    });

    // ---- Application: identity provider. BasicIdentityProvider is dev-only; a real application
    // brings its own and passes its issuer to the gateway. Its client registry is shared across
    // instances via Table Storage so a client that registers on one instance can obtain a token
    // from another (Flex Consumption).
    const identityProvider = new DboteIdentityProvider(this, environment, insights.connection);

    // Audience is just a logical identifier that must match between issuer and validator
    const audience = 'http://dbote-worker/api';

    identityProvider.addAppSetting('BasicIdentityProvider__Audience', audience);

    // ---- Gateway
    const gateway = new DboteGateway(this, environment, {
      subscriptionId,
      registry: { id: registry.id, loginServer: registry.loginServer },
      appConfigurationId: appConfiguration.id,
      appConfigurationEndpoint: appConfiguration.endpoint,
      appInsightsConnection: insights.connection,
      openId: {
        authority: identityProvider.endpoint,
        jwksUri: `${identityProvider.endpoint}/.well-known/jwks.json`,
        audience,
        requiredScope: 'bote',
      },
    });

    // ---- Application: the samples' queues and settings.
    new DboteSampleApp(this, environment, {
      gateway,
      appConfigurationId: appConfiguration.id,
      appInsightsConnection: insights.connection,
    });
  }
}

const app = new App();
const stack = new DboteStack(app, "infra");

new AzurermBackend(stack, {
  resourceGroupName: 'rg-dbote-test',
  storageAccountName: 'stdbotetfstatesp',
  containerName: 'tfstate',
  key: 'dbote.tfstate',
});
app.synth();
