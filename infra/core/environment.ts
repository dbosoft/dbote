/**
 * Where and as what a deployment is made, and how its resources are named:
 * `{type}-{appName}[-{suffix}]-{environment}`, e.g. `sbns-dbote-cmdev`. Give every application its
 * own appName so two gateway deployments in one subscription do not collide.
 */
export class Environnment {

    public readonly tenantId: string;
    public readonly environment: string;
    public readonly location: string;
    public readonly resourceGroup: string;
    public readonly appName: string;

    constructor(tenantId: string, location: string, resourceGroup: string, environment: string,
                appName: string = 'dbote') {
        this.tenantId = tenantId;
        this.location = location;
        this.resourceGroup = resourceGroup;
        this.environment = environment;
        this.appName = appName;
    }

    formatName(type: string, suffix: string = '') {
        return !!suffix
            ? `${type}-${this.appName}-${suffix}-${this.environment}`
            : `${type}-${this.appName}-${this.environment}`;
    }

    formatSafeName(type: string, suffix: string = '') {
        return this.formatName(type, suffix).toLowerCase().replace(/[^a-z0-9]/g, '');
    }
}
