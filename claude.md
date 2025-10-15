# dbosoft bote - Hybrid Cloud Messaging Gateway

## What is dbosoft bote?

**dbosoft bote** is a multi-tenant hybrid messaging gateway that bridges cloud services (Azure Service Bus) with on-premises/edge clients (Azure Storage Queues). It enables building SaaS applications with secure, economical, and accessible hybrid cloud connectivity.

### The Problem It Solves

Modern SaaS applications need to communicate with on-premises systems, but face several challenges:

1. **Polling Cost & Inefficiency**: Standard Rebus Azure Storage transport continuously polls Storage Queues (even when empty), generating expensive Azure transaction costs at scale
2. **Network Accessibility**: Azure Service Bus uses AMQP protocol (ports 5671/5672) which is blocked by corporate firewalls; requires VPN/ExpressRoute for on-premises access
3. **Infrastructure Exposure**: Directly exposing internal Service Bus queues to clients reveals architecture and creates security risks
4. **Multi-Tenant Economics**: Traditional per-tenant infrastructure (separate Service Bus namespaces, Functions, etc.) doesn't scale economically

### The Solution: DMZ/Gateway Pattern

bote creates a **security and abstraction boundary** between internal cloud architecture and customer clients:

```
┌─────────────────────────────────────────────────────┐
│ INTERNAL CLOUD (Private)                            │
│ ───────────────────────────────────────────         │
│ • Service Bus (push-based, high-throughput)         │
│   - Internal queues: orders, shipping, etc.         │
│   - Gateway queues: bote-cloud, bote-clients     │
│ • Databases, Redis, Internal Services (hidden)      │
│                                                      │
│ ┌─────────────────────────────────────────┐         │
│ │ BoteWorker (DMZ/Security Boundary)      │         │
│ │ ─────────────────────────────────────   │         │
│ │ • JWT Authentication                    │         │
│ │ • Tenant Identity Validation            │         │
│ │ • Message Routing & Isolation           │         │
│ │ • Security Enforcement                  │         │
│ └─────────────────────────────────────────┘         │
│                      │                               │
└──────────────────────┼───────────────────────────────┘
                       │
           ┌───────────▼──────────┐
           │  EXPOSED INTERFACE   │
           │  ─────────────────   │
           │  • Storage Queues    │
           │    (HTTPS, port 443) │
           │  • SignalR           │
           │    (WebSocket/HTTPS) │
           └──────────────────────┘
                       │
            ─────  INTERNET  ─────
                       │
           ┌───────────▼──────────────┐
           │  CUSTOMER PREMISES       │
           │  • Client (Rebus)     │
           │  • No Azure connectivity │
           │  • No VPN required       │
           └──────────────────────────┘
```

## Architecture Components

### Cloud Side: Service Bus + Rebus

**Used by**: Internal cloud services

- **Azure Service Bus**: High-throughput, push-based messaging for cloud-to-cloud communication
- **Rebus with ASB Transport**: Native Service Bus integration with all features (pub/sub, sessions, transactions)
- **Queues**:
  - `bote-cloud`: Cloud receives messages FROM clients
  - `bote-clients`: Cloud sends messages TO clients (single queue for all tenants)
  - Internal queues: Your application's internal architecture

**Benefits**:
- Push-based (Azure Functions trigger, no polling)
- Native auto-scaling
- High throughput, large messages (up to 1MB)
- Full Service Bus features

### Edge Side: Storage Queues + Custom Transport

**Used by**: On-premises/edge clients

- **Azure Storage Queues**: HTTPS-accessible queues with SAS token authentication
- **Custom Rebus Transport**: Eliminates wasteful polling via SignalR push notifications
- **Queues**: `bote-{tenantId}-{clientId}` (one per tenant-client, dynamically created)

**How it Works**:
1. **SignalR Push**: Worker sends instant notification when message arrives
2. **Smart Polling**: Client only polls Storage Queue when notified
3. **Near-Zero Idle Cost**: No polling when queue is empty

**Benefits**:
- HTTPS (port 443, universally accessible)
- Simple SAS token authentication
- No VPN/ExpressRoute setup required
- Efficient (only poll when messages exist)

### BoteWorker: Security Gateway (Azure Function)

**Purpose**: Enforce security, route messages, maintain tenant isolation

**Responsibilities**:
1. **Authentication**: Validates client JWT tokens (OAuth 2.0 Client Credentials flow with ECDSA-signed client assertions)
2. **Identity Injection**: Adds trusted `TenantId` + `ClientId` headers (clients cannot spoof these - see `Messages.cs:254-267`)
3. **Message Routing**:
   - **Targeted**: Routes to specific client queue (`bote-{tenant}-{client}`)
   - **Broadcast**: Routes to all clients subscribed to a topic within a tenant
4. **Isolation**: Ensures tenants cannot access each other's messages
5. **SignalR Notifications**: Sends push notifications to active clients

**Deployment**: Single shared Azure Function serves ALL tenants (economic multi-tenancy)

## Deployment Model

### Shared Infrastructure (One per SaaS Product)

**Managed by SaaS Provider:**

- **Azure Service Bus namespace** (one namespace serves all tenants)
- **BoteWorker** (Azure Function, auto-scales for all tenants)
- **Storage Account** (one account with many queues)
- **SignalR Service** (all tenant connections)
- **Identity Provider** (OAuth server for client authentication)
- **Cloud Services** (your SaaS application logic)

**Cost**: Fixed cost regardless of tenant count (Service Bus, Functions compute, Storage account)

### Per-Tenant Resources (Dynamically Created)

**Created on tenant onboarding:**

- **Storage Queues**: `bote-{tenantId}-{clientId}` (created on-demand, pennies per month)
- **Table Storage**: Subscription entries for topic broadcasts
- **Identity Provider**: Client registrations (public keys)

**Cost**: Near-zero per tenant (only pay for storage transactions/storage)

### Per Customer Site (Deployed by Customer)

**Deployed in customer infrastructure:**

- **Client Application** (runs on-premises/edge)
- **Private Signing Key** (ECDSA P-256, unique per client)

**No Azure connectivity required**: Clients access only Storage Queues via HTTPS

## Multi-Tenancy Economics

**Traditional Approach** (doesn't scale):
```
1000 tenants × (Service Bus namespace + Azure Functions + networking)
= 1000× infrastructure cost
```

**bote Approach** (scales economically):
```
1 Service Bus namespace + 1 Azure Function + 1000 Storage Queues
= ~1.1× cost (Storage Queues are pennies/month each)
```

## Usage

### Cloud Service Configuration

Cloud services use standard Rebus with Azure Service Bus, plus bote extensions:

```csharp
builder.Services.AddRebus((configure, serviceProvider) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
    return configure
        .Options(b => b.RetryStrategy(errorQueueName: options.Queues.Error))
        .Options(o => o.EnableBote(options.Queues.Clients))  // Enable bote multi-tenancy
        .Transport(t => t.UseAzureServiceBus(
            builder.Configuration.GetSection("dbote:Cloud:ServiceBus:Connection"),
            options.Queues.Cloud))
        .Serialization(s => s.UseSystemTextJson())
        .Logging(l => l.MicrosoftExtensionsLogging(...))
        .Routing(r => r.TypeBased()
            .Map<MessageToClient>($"{options.Queues.Clients}-client-id"));
});
```

**Key Points**:
- `.EnableBote()` adds tenant-aware pipeline steps and name formatter
- Use Service Bus connection (standard Rebus ASB transport)
- Route messages to `{clientsQueue}-{clientId}` (formatter redirects to shared queue)

### Sending Messages to Clients

#### Targeted Message (to specific client):

```csharp
// Cloud receives tenant/client identity from incoming message headers
var tenantId = MessageContext.Current.Headers[BoteHeaders.TenantId];
var clientId = MessageContext.Current.Headers[BoteHeaders.ClientId];

await bus.Send(new ResponseMessage(), new Dictionary<string, string>()
{
    [BoteHeaders.TenantId] = tenantId,
    [BoteHeaders.ClientId] = clientId,
});
```

**Flow**:
1. Cloud sends to `bote-clients` queue
2. BoteWorker routes to `bote-{tenantId}-{clientId}` Storage Queue
3. SignalR notifies specific client
4. Client polls queue and receives message

#### Broadcast Message (to all clients in tenant on a topic):

```csharp
var tenantId = MessageContext.Current.Headers[BoteHeaders.TenantId];

await bus.Send(new BroadcastMessage(), new Dictionary<string, string>()
{
    [BoteHeaders.TenantId] = tenantId,
    [BoteHeaders.Topic] = "chat",  // Topic name
});
```

**Flow**:
1. Cloud sends to `bote-clients` queue with Topic header
2. BoteWorker queries subscriptions table for clients subscribed to `{tenantId}/chat`
3. Sends message to each subscribed client's Storage Queue
4. SignalR notifies all subscribers
5. Active clients receive message (offline clients do not accumulate messages)

### Client Configuration

Clients use custom bote transport with Storage Queues:

```csharp
builder.Services.Configure<BoteOptions>(builder.Configuration.GetSection("dbote:Client"));

builder.Services.AddRebus((configure, serviceProvider) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<BoteOptions>>().Value;
    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

    return configure
        .Options(b => b.RetryStrategy(errorQueueName: options.Queues.Error))
        .Transport(t => t.UseBote(  // Custom bote transport
            new Uri(options.Endpoint),  // BoteWorker SignalR endpoint
            $"{options.Queues.Clients}-{options.ClientId}",  // Queue name
            new BoteCredentials
            {
                ClientId = options.ClientId,
                SigningKey = options.GetSigningKey(),  // ECDSA private key
                TenantId = options.TenantId,
                Authority = options.Authentication.Authority,
                TokenEndpoint = options.Authentication.TokenEndpoint,
                Scope = options.Authentication.Scope,
            },
            httpClientFactory))
        .Serialization(s => s.UseSystemTextJson())
        .Logging(l => l.MicrosoftExtensionsLogging(...))
        .Routing(r => r.TypeBased().Map<MessageToCloud>(options.Queues.Cloud));
});
```

**Key Points**:
- `.UseBote()` configures custom transport with SignalR integration
- Credentials include ECDSA signing key for JWT client assertions
- No Service Bus connection needed

### Subscribing to Topics

Clients use standard Rebus topic subscription API:

```csharp
var bus = app.Services.GetRequiredService<Rebus.Bus.IBus>();
await bus.Advanced.Topics.Subscribe("chat");
```

**What Happens**:
1. Rebus calls `BoteSubscriptionStorage.Subscribe()`
2. Storage calls SignalR's `SubscribeToTopic("chat")`
3. BoteWorker stores subscription in Table Storage: `{tenantId}/chat` → `{clientId}`
4. Future broadcasts to this topic include this client

## Security Model

### Client Authentication Flow

1. **Client**: Creates JWT client assertion signed with private ECDSA key
2. **Client**: Sends OAuth 2.0 Client Credentials request to Identity Provider
3. **Identity Provider**: Validates signature using public key (from JWKS endpoint)
4. **Identity Provider**: Issues access token with `tenant_id` and `client_id` claims
5. **Client**: Connects to SignalR with access token
6. **BoteWorker**: Validates access token against Identity Provider JWKS
7. **BoteWorker**: Extracts tenant/client identity from token claims
8. **BoteWorker**: Associates SignalR connection with verified identity

### Message Flow Security

**Cloud → Client:**
1. Cloud service adds `TenantId` + `ClientId` headers (from authenticated incoming message)
2. Message routed through Service Bus to BoteWorker
3. BoteWorker routes to correct Storage Queue based on headers
4. Only authenticated client can access their queue (SAS token scoped to specific queue)

**Client → Cloud:**
1. Client sends message via SignalR (authenticated connection)
2. **BoteWorker validates and REJECTS any tenant/client headers from client** (see `Messages.cs:254-267`)
3. **BoteWorker injects trusted headers** from authenticated SignalR connection claims
4. Cloud services trust headers because they came from BoteWorker, not client

**Key Security Properties**:
- Clients cannot spoof tenant/client identity
- Clients cannot access other tenants' messages
- Clients cannot see internal Service Bus infrastructure
- All client→cloud messages have verified identity

## Identity Provider

### BasicIdentityProvider (Development Only)

The included `BasicIdentityProvider` is a **minimal reference implementation** for development and testing:

**Features**:
- Issues OAuth 2.0 access tokens for clients
- Validates ECDSA-signed client assertions
- Exposes JWKS endpoint for public key discovery
- In-memory client registry

**Limitations** (not suitable for production):
- Ephemeral ECDSA key (regenerated on restart)
- In-memory storage (no persistence)
- No key rotation
- No caching
- No tenant/organization management

### Production Identity Provider

For production deployments, replace with a proper identity/auth service:

**Requirements**:
- **Persistent keys** stored in Azure Key Vault or HSM
- **Key rotation** with overlapping validity periods
- **Caching** (Redis) for token validation
- **JWKS endpoint** for public key discovery
- **Tenant management** (organization hierarchy, client registration)
- **Audit logging** (token issuance, authentication failures)
- **Rate limiting** and DDoS protection

**Integration**:
- BoteWorker validates tokens via standard OIDC/OAuth 2.0 token validation
- Configure `dbote:Worker:OpenId:Authority` and `dbote:Worker:OpenId:JwksUri` in BoteWorker settings
- Clients configure `TokenEndpoint` and `Authority` in client settings

## Repository Maintenance

### Key Areas

#### 1. Rebus Integration (`src/Dbosoft.Bote.Rebus.Integration`)

**Purpose**: Cloud-side extensions for multi-tenant message routing

**Key Files**:
- `BoteConfigurationExtensions.cs`: `.EnableBote()` configuration
- `BoteNameFormatter.cs`: Redirects `bote-clients-{id}` → `bote-clients`
- `BoteHeaders.cs`: Defines header constants (`TenantId`, `ClientId`, `Topic`, `Signature`)
- `Pipeline/BoteOutgoingStep.cs`: Ensures outgoing messages have tenant context
- `Pipeline/BoteOutgoingClientStep.cs`: Adds `ClientId` header or validates `Topic` broadcast
- `Pipeline/BoteIncomingStep.cs`: Validates incoming messages have tenant context

**When to Update**:
- Adding new routing patterns
- Adding new header-based features
- Changing queue naming conventions

#### 2. Custom Transport (`src/client/src/Dbosoft.Bote.Rebus`)

**Purpose**: Client-side transport with Storage Queues + SignalR

**Key Files**:
- `Transport/BoteTransport.cs`: Custom Rebus transport implementation
- `SignalRClient.cs`: Manages SignalR connection, authentication, and messaging
- `PendingMessagesIndicator.cs`: Tracks whether messages are available (eliminates wasteful polling)
- `Subscriptions/BoteSubscriptionStorage.cs`: Implements Rebus subscription API via SignalR

**When to Update**:
- Improving reconnection logic
- Optimizing polling behavior
- Adding transport-level features (compression, encryption)
- Implementing timeouts/deferred messages

#### 3. BoteWorker (`src/workers/src/Dbosoft.Bote.BoteWorker`)

**Purpose**: Security gateway and message router (Azure Function)

**Key Files**:
- `Messages.cs`: Azure Functions handlers
  - `Negotiate()`: SignalR connection negotiation with JWT validation
  - `ServiceBusReceivedMessageFunction()`: Routes Service Bus messages to Storage Queues
  - `SendMessage()`: Receives messages from clients via SignalR, validates, routes to Service Bus
  - `GetQueueMetadata()`: Provides SAS URI for client's Storage Queue
  - `SubscribeToTopic()` / `UnsubscribeFromTopic()`: Manages topic subscriptions
- `TokenValidationService.cs`: Validates client JWT tokens
- `ClientIdentity.cs`: Extracts tenant/client claims from tokens

**When to Update**:
- Changing authentication/authorization logic
- Adding new routing modes (beyond targeted + broadcast)
- Implementing rate limiting or throttling
- Adding audit logging

#### 4. Identity Provider (`src/workers/src/Dbosoft.Bote.BasicIdentityProvider`)

**Purpose**: Development-only OAuth server for client authentication

**Status**: ⚠️ **NOT FOR PRODUCTION** - Replace with proper identity service

**Key Files**:
- `Program.cs`: ASP.NET Core setup
- `TokenIssuer.cs`: Issues JWT access tokens
- `JwksEndpoint.cs`: Exposes public keys for token validation
- `InMemoryClientRepository.cs`: Stores client registrations (ephemeral)

**When to Update**:
- Adding more realistic development scenarios
- Improving client registration workflow for testing
- **Do NOT** extend for production use - replace instead

### Testing

#### Local Development

```bash
# From repository root
docker compose up -d

# Wait for services to be healthy
docker compose ps

# Run cloud service
cd samples/simple/Dbosoft.Bote.Samples.Simple.Cloud
dotnet run

# Run client (separate terminal)
cd samples/simple/Dbosoft.Bote.Samples.Simple.Client
dotnet run
```

#### Samples

- **Simple**: Basic request-response pattern (`samples/simple/`)
  - Client sends `PingMessage` → Cloud replies with `PongMessage`
  - Cloud pushes `PushMessage` to specific clients

- **Chat**: Multi-client broadcast pattern (`samples/chat/`)
  - Multiple clients per tenant
  - Topic-based broadcasting within tenant
  - Demonstrates tenant isolation

#### Benchmarks

Performance tests available in `test/benchmark/`:
- Message throughput
- Latency measurements
- Multi-tenant load testing

### Azure Deployment

The `infra/` folder contains **test infrastructure deployment** to Azure using CDKTF (Terraform):

**Prerequisites**:
- Docker, Azure CLI, Node.js 20+
- Azure subscription with appropriate permissions

**Deployment Steps**:
```bash
cd infra

# Install dependencies
npm install

# Login to Azure
az login

# Build artifacts (Azure Function zip, container images)
./Prepare-Artifacts.ps1

# Assign yourself Storage Blob Data Contributor role for Terraform state
# (storage account: stdbotestfstate)

# Build and synthesize Terraform
npm run build
npm run synth

# Apply infrastructure
cd cdktf.out/stacks/infra
terraform apply
```

**What Gets Deployed**:
- Azure Service Bus namespace
- Storage Account
- Azure Functions (BoteWorker)
- SignalR Service
- Application Insights
- Container Registry (for sample applications)

**Note**: This is for **testing/development** purposes. Production deployments should:
- Use separate environments (dev/staging/prod)
- Implement proper secrets management (Key Vault)
- Configure monitoring and alerting
- Set up CI/CD pipelines
- Replace BasicIdentityProvider with production auth service

### Common Maintenance Tasks

#### Adding a New Message Type

1. Define message class in shared messages project
2. Cloud: Add routing in `.Routing(r => r.TypeBased().Map<NewMessage>(...))`)
3. Cloud: Add handler implementing `IHandleMessages<NewMessage>`
4. Client: Add routing and handler
5. Test with samples

#### Changing Queue Names

1. Update configuration: `ServiceBusOptions.Queues.*`
2. Update BoteWorker configuration: `dbote:Worker:ServiceBus:Queues:*`
3. Update docker-compose.yaml for local development
4. Update infra deployment scripts

#### Adding New Client Features

1. Add SignalR method to `Messages.cs` in BoteWorker
2. Add corresponding method to `ISignalRClient` interface
3. Implement in `SignalRClient.cs`
4. Update `BoteTransport.cs` if transport-level changes needed
5. Test with sample client

#### Debugging

**Enable detailed logging**:
- Cloud: `builder.Services.AddLogging(c => c.AddSimpleConsole().SetMinimumLevel(LogLevel.Debug))`
- Client: Same as above
- BoteWorker: Already configured for Debug level in ApplicationInsights

**Common Issues**:
- **Authentication failures**: Check signing key format, token endpoint URL, JWKS endpoint accessibility
- **Messages not routing**: Check `TenantId`/`ClientId` headers, verify queue names
- **Client not receiving messages**: Check SignalR connection status, verify subscription to topics
- **High polling costs**: Ensure `PendingMessagesIndicator` is working (should see "no messages" logs, not continuous polling)

#### Azure Functions Queue Triggers - Client Configuration Requirements

**Critical Rule**: Azure Functions queue triggers require a specifically configured `QueueServiceClient` with **Base64 message encoding** and matching connection string. Any queue used with `[QueueTrigger]` MUST be created using the correctly configured client.

**Why This Matters**:
1. **Message Encoding**: Azure Functions expects Base64-encoded queue messages. The `AzureWebJobsStorage` client is configured with `MessageEncoding = Base64`, while the default application client is NOT. Using the wrong client results in incompatible message encoding.
2. **Connection String Matching**: The `[QueueTrigger(Connection = "AzureWebJobsStorage")]` attribute tells Azure Functions which connection string to use. The queue must be created using the same connection.
3. **Failure Symptom**: Using the wrong client causes Azure Functions to fail deserializing messages immediately, moving them to poison queue after 5 attempts with NO logs from inside the function.

**The Pattern**:

```csharp
// WRONG - Creates queue in application storage, trigger won't see it
public async Task EnqueueJob(QueueServiceClient queueServiceClient)  // Default injected client
{
    var queue = queueServiceClient.GetQueueClient("my-trigger-queue");
    await queue.SendMessageAsync(...);  // Goes to dbote:Worker:Storage:Connection
}

[Function("ProcessJob")]
public async Task ProcessJob(
    [QueueTrigger("my-trigger-queue", Connection = "AzureWebJobsStorage")]  // Monitors AzureWebJobsStorage
    string message)
{
    // This will NEVER execute - watching wrong storage account!
}

// CORRECT - Creates queue in same storage account as trigger
public async Task EnqueueJob(IAzureClientFactory<QueueServiceClient> queueClientFactory)
{
    var functionClient = queueClientFactory.CreateClient("AzureWebJobsStorage");
    var queue = functionClient.GetQueueClient("my-trigger-queue");
    await queue.SendMessageAsync(...);  // Goes to AzureWebJobsStorage
}

[Function("ProcessJob")]
public async Task ProcessJob(
    [QueueTrigger("my-trigger-queue", Connection = "AzureWebJobsStorage")]
    string message)
{
    // This WILL execute - both use AzureWebJobsStorage
}
```

**When to Use Each Client**:

1. **`queueClientFactory.CreateClient("AzureWebJobsStorage")`**:
   - For **infrastructure queues** monitored by Azure Functions triggers
   - Examples: `databus-copy-monitor` queue
   - **Configured with Base64 message encoding** (required by Azure Functions)
   - Must match the `Connection` parameter in `[QueueTrigger]`
   - May point to same storage account as application queues, but encoding differs

2. **Default injected `QueueServiceClient`** (from line 52 in Program.cs):
   - For **application data queues** NOT monitored by triggers
   - Examples: `bote-{tenant}-{client}` message queues
   - **No Base64 encoding** (default encoding)
   - Uses `dbote:Worker:Storage:Connection` configuration
   - Never use this for queues with `[QueueTrigger]` - encoding mismatch!

**Client Configuration in Program.cs**:

Both clients are registered in `Program.cs` (lines 48-58):
```csharp
builder.Services.AddAzureClients(clientBuilder =>
{
    // Default client - NO Base64 encoding (application data queues)
    clientBuilder.AddQueueServiceClient(
        builder.Configuration.GetSection("dbote:Worker:Storage:Connection"));

    // Named client - WITH Base64 encoding (Azure Functions infrastructure queues)
    clientBuilder.AddQueueServiceClient(builder.Configuration.GetSection("AzureWebJobsStorage"))
        .WithName("AzureWebJobsStorage")
        .ConfigureOptions(options =>
            options.MessageEncoding = Azure.Storage.Queues.QueueMessageEncoding.Base64);
});
```

**Real Example from Codebase**:

See `Messages.AttachmentUploaded` (lines 708-711) for correct pattern:
```csharp
// MUST use named client for Base64 encoding
var functionQueueClient = queueClientFactory.CreateClient("AzureWebJobsStorage");
var monitorQueue = functionQueueClient.GetQueueClient("databus-copy-monitor");
await monitorQueue.CreateIfNotExistsAsync();
await monitorQueue.SendMessageAsync(JsonSerializer.Serialize(copyRequest));
```

This queue is monitored by `DataBusCopyFile.DataBusCopyFileMonitor`:
```csharp
[QueueTrigger("databus-copy-monitor", Connection = "AzureWebJobsStorage")]
```

**Symptom of This Bug**:
- Messages reach `MaxDequeueCount` and move to poison queue immediately (5 failed attempts)
- NO logs from inside the triggered function (not even first log line)
- Azure Functions can't deserialize the incorrectly-encoded message
- Even if both connection strings point to the same storage account, encoding mismatch causes failure

## Key Design Decisions

### Single Service Bus Queue for All Clients

**Decision**: Route all client-bound messages through one queue (`bote-clients`)

**Rationale**:
- Azure Functions with Service Bus triggers require fixed queue names for auto-scaling
- Dynamically creating queues per tenant would require polling (Event Grid triggers need Premium SKU)
- Worker routes to individual Storage Queues after security validation

### Storage Queues for Client Access

**Decision**: Use Storage Queues (not Service Bus) for client-to-worker communication

**Rationale**:
- HTTPS accessibility (port 443) vs. AMQP (ports 5671/5672 often blocked)
- Simple SAS token authentication (no complex networking)
- Economical at scale (pennies per queue vs. dollars for Service Bus)

### SignalR Push Notifications

**Decision**: Use SignalR to notify clients of new messages instead of continuous polling

**Rationale**:
- Eliminates wasteful polling of empty queues (reduces Azure transaction costs)
- Lower latency (instant notification vs. polling interval)
- Maintains Rebus transport abstraction (clients use standard Rebus API)

### Tenant-Aware Headers

**Decision**: Inject `TenantId` and `ClientId` as message headers at security boundary

**Rationale**:
- Prevents identity spoofing (clients cannot set these headers)
- Enables shared infrastructure with isolated routing
- Cloud services can trust identity in headers
- Simple programming model (headers, not custom API)

### Topic Broadcast via Table Storage

**Decision**: Use Table Storage for subscription management + on-demand delivery

**Rationale**:
- Reactive pattern: Only active clients receive broadcasts
- No accumulated messages for offline clients (they don't expect historical broadcasts)
- Multi-instance safe (no in-memory state)
- Economical (queries are cheap)

## Contributing

When contributing to this repository:

1. **Maintain Security Boundaries**: Never expose internal infrastructure to clients
2. **Preserve Multi-Tenancy**: All features must support tenant isolation
3. **Test with Samples**: Verify changes with both simple and chat samples
4. **Document Configuration**: Update this file and README.md for config changes
5. **Consider Scale**: Features should work with 1000+ tenants on shared infrastructure

## License

[Add license information here]
