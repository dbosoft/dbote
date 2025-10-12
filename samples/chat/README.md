# SuperBus Chat Sample

A multi-tenant chat application demonstrating SuperBus's tenant isolation and real-time messaging capabilities.

## Demo Scenarios

### Scenario 1: Multi-Tenant Docker Demo (Recommended)

**Quick Start (Windows):**
```powershell
# From samples/chat directory
.\Start-Services.ps1
```

**Quick Start (Linux/macOS):**
```bash
# From samples/chat directory
docker compose up --build
```

**What gets deployed:**
- 3 chat connector web apps (Blazor):
  - **Tenant A - Connector A**: http://localhost:5000
  - **Tenant A - Connector B**: http://localhost:5001
  - **Tenant B - Connector A**: http://localhost:5002
- Cloud service (message relay)
- SuperBus infrastructure (worker, identity provider, storage, service bus)

**Demo Flow:**
1. Open http://localhost:5000 and http://localhost:5001 in separate browser windows
2. Send a message from either connector → ✅ **Message broadcasts to all connectors in Tenant A**
   - Both connector-a (port 5000) and connector-b (port 5001) receive the message
3. Open http://localhost:5002 in a third window (Tenant B)
4. Send a message from 5002 → ❌ **Messages isolated from Tenant A**
   - Only Tenant B connectors receive the message (just connector-a on port 5002)
   - Tenant A connectors (ports 5000/5001) do NOT receive it

**Key Behaviors:**
- **Broadcast within tenant**: Messages are delivered to ALL connectors in the same tenant
- **Tenant isolation**: Messages never cross tenant boundaries
- **Author tracking**: Each message shows which connector sent it

### Scenario 2: Local Development

**Prerequisites:**
- .NET 8 SDK
- Docker (for infrastructure only)

**Setup:**
```bash
# From repository root, start infrastructure containers
docker compose up -d

# Wait for services to be healthy (~30 seconds)
docker compose ps

# From samples/chat/SuperBus.Samples.Chat.Connector directory
dotnet run
```

**Configuration:**
Edit `appsettings.Development.json` to change tenant/connector:
```json
{
  "SuperBus": {
    "Connector": {
      "TenantId": "tenant-a",        // Change this
      "ConnectorId": "connector-a",  // Change this
      "Authentication": {
        "Authority": "http://localhost:7250",
        "TokenEndpoint": "http://localhost:7250/api/tenant-a/token",  // Match tenant
        "Scope": "superbus",
        "SigningKey": "..."
      }
    }
  }
}
```

**Note:** For local development with multiple connectors, you'll need to:
1. Run multiple instances with different `appsettings.Development.json` files
2. Or use the Docker demo (Scenario 1) which is easier

## Architecture

```
┌─────────────────────┐     ┌─────────────────────┐     ┌─────────────────────┐
│  Connector A        │     │  Connector B        │     │  Connector (Tenant B)│
│  (Tenant A)         │     │  (Tenant A)         │     │                     │
│  Port 5000          │     │  Port 5001          │     │  Port 5002          │
└──────────┬──────────┘     └──────────┬──────────┘     └──────────┬──────────┘
           │                           │                           │
           │ ┌─────────────────────────┴───────────────────────┐   │
           └─►  SuperBus Worker                                 ◄───┘
             │  - Routes messages by tenant                     │
             │  - Enforces tenant isolation                     │
             └──────────────────┬───────────────────────────────┘
                                │
                       ┌────────▼────────┐
                       │  Cloud Service  │
                       │  - Receives chat messages       │
                       │  - Sends to Connectors queue    │
                       │    without ConnectorId          │
                       └─────────────────┘
```

**Message Flow:**
1. Connector sends `ChatRequest` to Cloud queue
2. Cloud service receives request with authenticated tenant/connector identity
3. Cloud sends **ONE** `ChatResponse` to Connectors queue with `TenantId` and `Topic: "chat"` (no `ConnectorId`)
4. Worker detects topic broadcast mode (has `Topic` header)
5. Worker stores message in Azure Table Storage with 5-minute expiration
6. Worker schedules cleanup via Storage Queue (visibility timeout: 5 min)
7. Worker notifies SignalR group: `topic/{tenantId}/chat`
8. Active connectors receive `BroadcastAvailable` notification via SignalR
9. Connectors request the broadcast message via `RequestBroadcast(messageId)`
10. Worker creates Storage Queue entry for each requesting connector
11. Connectors poll their queues and display the message (existing mechanism)
12. After 5 minutes, cleanup function deletes broadcast from Table Storage

**Topic-Based Broadcast Architecture:**
- **Reactive pull pattern**: Only active connectors receive messages (not stored for offline connectors)
- **SignalR groups**: Connectors subscribe to topics (e.g., "chat") to receive notifications
- **Shared storage**: Azure Table Storage for broadcasts, Storage Queue for cleanup
- **Multi-instance safe**: No in-memory state, supports multiple worker instances
- **Tenant isolation**: Topic groups include tenantId: `topic/{tenantId}/{topic}`
- **Automatic cleanup**: Storage Queue visibility timeout triggers cleanup after expiration

## Multi-Tenant Security

Each connector authenticates with:
- **Client Assertion JWT** (signed with connector's private key)
- **Tenant-specific token endpoint**: `/api/{tenantId}/token`
- **Standards-compliant OAuth 2.0 flow** (RFC 7523)

The SuperBus Worker:
- Validates all JWTs against the identity provider's JWKS
- Injects authenticated tenant/connector identity into messages
- **Prevents header injection attacks** (connectors cannot spoof identity)
- **Routes topic broadcasts** via SignalR groups and Table Storage
- Uses reactive pull pattern: only active connectors receive broadcasts
- Routes messages only within tenant boundaries

## Cleanup

```bash
# Stop and remove containers
docker compose down

# Remove volumes (resets all data)
docker compose down -v
```

## Troubleshooting

**Connectors fail to start with "Authority is not configured"**
- The docker-compose.yaml should have all required configuration
- For local dev, ensure appsettings.Development.json has Authority, TokenEndpoint, and Scope

**Messages not being delivered**
- Check all services are healthy: `docker compose ps`
- Check connector logs: `docker compose logs tenant-a-connector-a`
- Verify connectors are on the same tenant to communicate

**Port already in use**
- Change ports in docker-compose.yaml if 5000-5002 are occupied
