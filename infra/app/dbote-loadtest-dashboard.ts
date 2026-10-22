import { Construct } from "constructs";
import { Environnment } from "../core";
import { ServicePlan } from "@cdktf/provider-azurerm/lib/service-plan";
import { LinuxWebApp } from "@cdktf/provider-azurerm/lib/linux-web-app";

export interface DboteLoadTestDashboardConfig {
    serviceBusConnectionString: string;
    storageConnectionString: string;
    cloudQueue: string;
    clientsQueue: string;
    appInsightsConnection: string;
}

/**
 * The load-test consolidator/dashboard as a single Linux Web App (App Service). It is a push target:
 * every controller replica and edge generator posts its metrics here, and it serves one aggregated
 * live dashboard. Single instance on purpose (in-memory aggregation, always-on).
 */
export class DboteLoadTestDashboard extends Construct {

    public readonly url: string;

    constructor(scope: Construct, environment: Environnment, config: DboteLoadTestDashboardConfig) {
        super(scope, environment.formatName('cdktf', 'ltdash'));

        const plan = new ServicePlan(this, environment.formatName('asp', 'dash'), {
            name: environment.formatName('asp', 'dash'),
            location: environment.location,
            resourceGroupName: environment.resourceGroup,
            osType: 'Linux',
            skuName: 'B1',
        });

        const app = new LinuxWebApp(this, environment.formatName('app', 'dash'), {
            name: environment.formatName('app', 'dash'),
            location: environment.location,
            resourceGroupName: environment.resourceGroup,
            servicePlanId: plan.id,
            siteConfig: {
                alwaysOn: true,
                applicationStack: { dotnetVersion: '8.0' },
            },
            appSettings: {
                'dbote__Cloud__ServiceBus__Connection': config.serviceBusConnectionString,
                'dbote__Cloud__Storage__Connection': config.storageConnectionString,
                'dbote__Cloud__ServiceBus__Queues__Cloud': config.cloudQueue,
                'dbote__Cloud__ServiceBus__Queues__Clients': config.clientsQueue,
                'LoadTest__EdgeQueuePrefix': 'bote-load-',
                'APPLICATIONINSIGHTS_CONNECTION_STRING': config.appInsightsConnection,
            },
        });

        this.url = `https://${app.defaultHostname}`;
    }
}
