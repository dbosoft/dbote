import { Construct } from "constructs";
import { Environnment } from "../environment";
import { RoleAssignment } from "@cdktf/provider-azurerm/lib/role-assignment";
import { DataAzurermContainerRegistry } from "@cdktf/provider-azurerm/lib/data-azurerm-container-registry";
import { ContainerApp } from "@cdktf/provider-azurerm/lib/container-app";
import { ContainerAppEnvironment } from "@cdktf/provider-azurerm/lib/container-app-environment";
import { UserAssignedIdentity } from "@cdktf/provider-azurerm/lib/user-assigned-identity";

export class DboteBenchmark extends Construct {

    public readonly cloudPrincipalId: string;
    public readonly servicePrincipalId: string;

    constructor(scope: Construct, environment: Environnment, appConfigConnection: string, appInsightsConnection: string) {
        super(scope, environment.formatName('cdktf', 'benchmark'));

        const containerRegistry = new DataAzurermContainerRegistry(this, 'acrbotetest', {
            name: 'acrbotetest',
            resourceGroupName: environment.resourceGroup,
        })

        const appEnvironment = new ContainerAppEnvironment(this, environment.formatName('cae'), {
            name: environment.formatName('cae'),
            location: environment.location,
            resourceGroupName: environment.resourceGroup,
            workloadProfile:[
                {
                    name: 'Consumption',
                    workloadProfileType: 'Consumption',
                },
            ],
            // TODO investigate logging
        });

        const acrIdentity = new UserAssignedIdentity(this, environment.formatName('id', 'ca'), {
            name: environment.formatName('id', 'ca'),
            location: environment.location,
            resourceGroupName: environment.resourceGroup,
        });

        const acrIdentityRole = new RoleAssignment(this, environment.formatName('role', 'id-ca-acr'), {
            scope: containerRegistry.id,
            // 'Container Registry Repository Reader' whebn using RBAC+ABAC
            roleDefinitionName: 'AcrPull',
            principalId: acrIdentity.principalId,
        })

        const cloudApp = new ContainerApp(this, environment.formatName('ca', 'cloud'), {
            name: environment.formatName('ca', 'cloud'),
            resourceGroupName: environment.resourceGroup,
            containerAppEnvironmentId: appEnvironment.id,
            revisionMode: 'Single',
            dependsOn: [acrIdentityRole],
            template: {
                container: [
                    {
                        name: environment.formatName('ca', 'cloud'),
                        image: 'acrbotetest.azurecr.io/dbote-benchmark/cloud:latest',
                        cpu: 0.25,
                        memory: '0.5Gi',
                        env: [
                            {
                                name: 'APPLICATIONINSIGHTS_CONNECTION_STRING',
                                value: appInsightsConnection,
                            },
                            {
                                name: 'dbote__AppConfiguration__Endpoint',
                                value: appConfigConnection,
                            },
                            {
                                name: 'dbote__AppConfiguration__Environment',
                                value: environment.environment,
                            },
                            {
                                name: 'dbote__AppConfiguration__Prefix',
                                value: 'dbote:Cloud',
                            },
                        ],
                    },
                ],
                minReplicas: 1,
                maxReplicas: 1,
            },
            identity: {
                type: 'SystemAssigned, UserAssigned',
                identityIds: [acrIdentity.id],
            },
            registry: [
                {
                    server: containerRegistry.loginServer,
                    identity: acrIdentity.id,
                }
            ]
        });

        this.cloudPrincipalId = cloudApp.identity.principalId;

        const serviceApp = new ContainerApp(this, environment.formatName('ca', 'service'), {
            name: environment.formatName('ca', 'service'),
            resourceGroupName: environment.resourceGroup,
            containerAppEnvironmentId: appEnvironment.id,
            revisionMode: 'Single',
            dependsOn: [acrIdentityRole],
            template: {
                container: [
                    {
                        name: environment.formatName('ca', 'service'),
                        image: 'acrbotetest.azurecr.io/dbote-benchmark/service:latest',
                        cpu: 0.25,
                        memory: '0.5Gi',
                        env: [
                            {
                                name: 'APPLICATIONINSIGHTS_CONNECTION_STRING',
                                value: appInsightsConnection,
                            },
                            {
                                name: 'dbote__AppConfiguration__Endpoint',
                                value: appConfigConnection,
                            },
                            {
                                name: 'dbote__AppConfiguration__Environment',
                                value: environment.environment,
                            },
                            {
                                name: 'dbote__AppConfiguration__Prefix',
                                value: 'dbote:Service',
                            },
                        ],
                    },
                ],
                minReplicas: 1,
                maxReplicas: 1,
            },
            identity: {
                type: 'SystemAssigned, UserAssigned',
                identityIds: [acrIdentity.id],
            },
            registry: [
                {
                    server: containerRegistry.loginServer,
                    identity: acrIdentity.id,
                }
            ]
        });

        this.servicePrincipalId = serviceApp.identity.principalId;
    }
}