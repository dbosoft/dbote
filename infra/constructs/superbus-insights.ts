import { Construct } from "constructs";
import { Environnment } from "../environment";
import { LogAnalyticsWorkspace } from "@cdktf/provider-azurerm/lib/log-analytics-workspace";
import { ApplicationInsights } from "@cdktf/provider-azurerm/lib/application-insights";
import { monitorSmartDetectorAlertRule } from "@cdktf/provider-azurerm";
import { MonitorSmartDetectorAlertRule } from "@cdktf/provider-azurerm/lib/monitor-smart-detector-alert-rule";
import { MonitorActionGroup } from "@cdktf/provider-azurerm/lib/monitor-action-group";

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

    // Normally, Azure automatically creates an action group when creating the Application Insights resource.
    // This can cause issue when destroying the resources.
    // We manually create an action group with the same name.
    // See https://github.com/hashicorp/terraform-provider-azurerm/issues/18026.
    // new MonitorActionGroup(this, environment.formatName('ag', 'smart-detect'), {
    //   resourceGroupName: environment.resourceGroup,
    //   name: 'Application Insights Smart Detection',
    //   shortName: 'SmartDetect',
    //   enabled: false,
    // });
  }

  get connection(): string {
    return this.applicationInsights.connectionString;
  }
}
