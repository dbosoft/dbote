const appName: string = 'superbus'

export class Environnment {

    public readonly environment: string;
    public readonly location: string;
    public readonly resourceGroup: string;

    constructor(location: string, resourceGroup: string, environment: string) {
        this.location = location;
        this.resourceGroup = resourceGroup;
        this.environment = environment;
    }

    formatName(type: string, suffix: string = '') {
        return !!suffix
            ? `${type}-${appName}-${suffix}-${this.environment}`
            : `${type}-${appName}-${this.environment}`;
    }

    formatSafeName(type: string, suffix: string = '') {
        return this.formatName(type, suffix).toLowerCase().replace(/[^a-z0-9]/g, '');
    }
}
