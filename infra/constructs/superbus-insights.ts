import { Construct } from "constructs";
import { Environnment } from "../environment";
import { LogAnalyticsWorkspace } from "@cdktf/provider-azurerm/lib/log-analytics-workspace";
import { ApplicationInsights } from "@cdktf/provider-azurerm/lib/application-insights";

export class SuperBusInsights extends Construct {

  private readonly applicationInsights: ApplicationInsights;

  constructor(scope: Construct, environment: Environnment) {
    super(scope, environment.formatName('cdktf', 'insights'));

    const logAnalyticsWorkspace = new LogAnalyticsWorkspace(this, environment.formatName('log'), {
      name: environment.formatName('log'),
      location: environment.location,
      resourceGroupName: environment.resourceGroup,
      sku: 'PerGB2018',
      retentionInDays: 30,
      dailyQuotaGb: 10,
    });

    this.applicationInsights = new ApplicationInsights(this, environment.formatName('appi'), {
      name: environment.formatName('appi'),
      location:  environment.location,
      resourceGroupName: environment.resourceGroup,
      retentionInDays: 30,
      dailyDataCapInGb: 10,
      applicationType: 'web',
      workspaceId: logAnalyticsWorkspace.id,
    });
  }

  get connection(): string {
    return this.applicationInsights.connectionString;
  }
}
