# dbote

## Local Development

### Prerequisites
- Docker Desktop
- .NET 8 SDK

### Starting the Development Environment

The project uses Docker Compose to run all required emulators and services:

```bash
docker-compose up -d
```

This starts the following services:

- **Azure Service Bus Emulator** - Ports 5672 (AMQP), 5300 (HTTP)
- **SQL Server** - Port 1433
- **Azurite (Storage Emulator)** - Ports 10000 (Blob), 10001 (Queue), 10002 (Table)
  - Connection string: `UseDevelopmentStorage=true`
- **Azure SignalR Emulator** - Port 8888
- **Basic Identity Provider** - Port 7071
  - Token endpoint: `POST http://localhost:7071/token`
  - JWKS endpoint: `GET http://localhost:7071/.well-known/jwks.json`

### Basic Identity Provider

The BasicIdentityProvider is a simple sample implementation that demonstrates the token issuance pattern for connector authentication. It:

- Issues access tokens for connectors using OAuth 2.0 Client Credentials flow
- Validates connector client assertions (JWT signed with connector's private key)
- Exposes a JWKS endpoint for public key discovery
- Uses an ephemeral ECDSA P-256 key (generated on startup)

**Note:** This is a minimal reference implementation. Production deployments should use a separate identity/auth service with:
- Persistent keys stored in Azure Key Vault
- Caching with Redis
- Proper key rotation
- Integration with organization/tenant management

The dbote Worker validates tokens from the BasicIdentityProvider by fetching public keys from its JWKS endpoint.

## Design

### Flow of a message

#### Cloud -> Connector
1. Cloud: send message to `bote-connectors-connector1` with tenant header
2. Rebus pipeline: attach connector ID header
3. Rebus ASB name formatter: change queue to `bote-connectors`
4. dbote worker: take message from `bote-connectors` and copy it to Azure Queue Storage `bote-tenant1-connector1`
5. Connector: fetch message from AQS `bote-tenant1-connector-1`

## Important Technical Decisions

### Use single ASB queue for all connectors
The messages for all connectors of all tenants are handled by a single ASB queue.

#### Reasons
- The ASB trigger in Azure functions requires a fixed name. We want to use the ASB trigger
  to ensure that we can make use of the automatic scaling and function activation by Azure.
- Polling dynamically defined ASB queues would require the activation of the function with
  event grid triggers. Event grid triggers are only available in the premium SKU of ASB.

### Connector authentication
The connectors authenticate to the dbote infrastructure with a client assertion JWT which
is signed with a ECDSA key which is unique to the connector.

#### Reasons
- The connector might be deployed for extended periods and hence a static API key might not be
  considered secure enough.
- The ECDSA key could protected e.g. by the Windows key store if required
- The ECDSA key could be used to sign the messages themselves in case additional authentication
  is required.

### Use Application Insights SDK instead of Open Telemetry SDK
There are two SDKs to integrate with Azure Monitor: Application Insights and Open Telemetry.
Microsoft recommends to use the new Open Telemetry SDK for ASP.NET Core. For now, we using
the Application Insights SDK.

#### Reasons
- The Open Telemetry SDK for Azure Functions is still in Preview and missing features.
  This means we would need to mix SDKs.
- The Visual Studio integration seemingly no longer works with the Open Telemetry SDK.
  We would need to use a separate dashboard for Open Telemetry (e.g. from .NET Aspire)
  but that would not support the Application Insights SDK in the function app.
