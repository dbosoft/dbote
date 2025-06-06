import { Construct } from "constructs";
import { Environnment } from "../environment";

export class SuperBusBenchmark extends Construct {

    constructor(scope: Construct, environment: Environnment, appConfigConnection: string, appInsightsConnection: string) {
        super(scope, environment.formatName('cdktf', 'worker'));

        const a = appConfigConnection;
        const b = appInsightsConnection;
    }
}