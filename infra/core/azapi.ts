import { Construct } from "constructs";
import { IResolvable, ITerraformDependable, TerraformProvider, TerraformResource } from "cdktf";

// Minimal hand-written bindings for the azapi provider. There is no prebuilt
// @cdktf/provider-azapi package and the bindings `cdktf get` generates (.gen/) cannot ship
// inside this library, so only what the core needs is declared here. The synthesized JSON
// is the same as the generated bindings produce.

const generatorMetadata = {
    providerName: 'azapi',
    providerVersion: '2.11.0',
    providerVersionConstraint: '~> 2.0',
};

/** The azapi provider with its default configuration (credentials come from the environment). */
export class AzapiProvider extends TerraformProvider {
    constructor(scope: Construct, id: string) {
        super(scope, id, {
            terraformResourceType: 'azapi',
            terraformGeneratorMetadata: generatorMetadata,
            terraformProviderSource: 'Azure/azapi',
        });
    }

    protected synthesizeAttributes(): { [name: string]: any } {
        return {};
    }
}

export interface AzapiResourceIdentity {
    readonly type: string;
    readonly identityIds?: string[];
}

export interface AzapiResourceConfig {
    /** The resource type with its API version, e.g. `Microsoft.App/containerApps@2024-10-02-preview`. */
    readonly type: string;
    readonly name: string;
    readonly parentId: string;
    readonly location?: string;
    readonly schemaValidationEnabled?: boolean;
    readonly identity?: AzapiResourceIdentity[];
    readonly body: { [key: string]: any };
    readonly responseExportValues?: { [key: string]: string };
    readonly dependsOn?: ITerraformDependable[];
}

/** An `azapi_resource`: any ARM resource, given as its request body. */
export class AzapiResource extends TerraformResource {
    private readonly config: AzapiResourceConfig;

    constructor(scope: Construct, id: string, config: AzapiResourceConfig) {
        super(scope, id, {
            terraformResourceType: 'azapi_resource',
            terraformGeneratorMetadata: generatorMetadata,
            dependsOn: config.dependsOn,
        });
        this.config = config;
    }

    /** The values selected by `responseExportValues`. */
    get output(): IResolvable {
        return this.interpolationForAttribute('output');
    }

    protected synthesizeAttributes(): { [name: string]: any } {
        return {
            body: this.config.body,
            identity: this.config.identity?.map(i => ({ type: i.type, identity_ids: i.identityIds })),
            location: this.config.location,
            name: this.config.name,
            parent_id: this.config.parentId,
            response_export_values: this.config.responseExportValues,
            schema_validation_enabled: this.config.schemaValidationEnabled,
            type: this.config.type,
        };
    }
}
