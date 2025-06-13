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

    // Azure automatically creates an alert rule and action group a couple of minutes after
    // an Application Insights resouce has been created.
    // This causes issues when working with Terraform (or IaC in general) as these resources
    // are not managed by Terraform.
    // See https://github.com/hashicorp/terraform-provider-azurerm/issues/18026.
    // The feature application_insights->disable_generated_rule causes the provider to just
    // wait for 10 minutes until Azure has created the smart detection rule in the background
    // and then disable it. We instead just create a rule with the same name as Azure would use.
    // This prevents the generation of the alert rule and action group by Azure.
    new MonitorSmartDetectorAlertRule(this, environment.formatName('alert'), {
      name: `Failure Anomalies - ${this.applicationInsights.name}`,
      resourceGroupName: environment.resourceGroup,
      detectorType: 'FailureAnomaliesDetector',
      enabled: false,
      scopeResourceIds: [this.applicationInsights.id],
      frequency: 'PT1M',
      severity: 'Sev3',
      actionGroup: {
        ids: [],
      },
    })
  }

  get connection(): string {
    return this.applicationInsights.connectionString;
  }
}
