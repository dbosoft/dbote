# dbote Chat Sample

A multi-tenant chat application demonstrating dbote's tenant isolation and real-time messaging capabilities.

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
- 3 chat client web apps (Blazor):
  - **Tenant A - Client A**: http://localhost:5000
  - **Tenant A - Client B**: http://localhost:5001
  - **Tenant B - Client A**: http://localhost:5002
- Cloud service (message relay)
- dbote infrastructure (worker, identity provider, storage, service bus)

**Demo Flow:**
1. Open http://localhost:5000 and http://localhost:5001 in separate browser windows
2. Send a message from either client → ✅ **Message broadcasts to all clients in Tenant A**
   - Both client-a (port 5000) and client-b (port 5001) receive the message
3. Open http://localhost:5002 in a third window (Tenant B)
4. Send a message from 5002 → ❌ **Messages isolated from Tenant A**
   - Only Tenant B clients receive the message (just client-a on port 5002)
   - Tenant A clients (ports 5000/5001) do NOT receive it

**Key Behaviors:**
- **Broadcast within tenant**: Messages are delivered to ALL clients in the same tenant
- **Tenant isolation**: Messages never cross tenant boundaries
- **Author tracking**: Each message shows which client sent it

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

# From samples/chat/Dbosoft.Bote.Samples.Chat.Client directory
dotnet run
```

**Configuration:**
Edit `appsettings.Development.json` to change tenant/client:
```json
{
  "dbote": {
    "Client": {
      "TenantId": "tenant-a",        // Change this
      "ClientId": "client-a",  // Change this
      "Authentication": {
        "Authority": "http://localhost:7250",
        "TokenEndpoint": "http://localhost:7250/api/tenant-a/token",  // Match tenant
        "Scope": "dbote",
        "SigningKey": "..."
      }
    }
  }
}
```

**Note:** For local development with multiple clients, you'll need to:
1. Run multiple instances with different `appsettings.Development.json` files
2. Or use the Docker demo (Scenario 1) which is easier

## Architecture

```
┌─────────────────────┐     ┌─────────────────────┐     ┌─────────────────────┐
│  Client A           │     │  Client B           │     │  Client (Tenant B)  │
│  (Tenant A)         │     │  (Tenant A)         │     │                     │
│  Port 5000          │     │  Port 5001          │     │  Port 5002          │
└──────────┬──────────┘     └──────────┬──────────┘     └──────────┬──────────┘
           │                           │                           │
           │ ┌─────────────────────────┴───────────────────────┐   │
           └─►  dbote Worker                                    ◄───┘
             │  - Routes messages by tenant                     │
             │  - Enforces tenant isolation                     │
             └──────────────────┬───────────────────────────────┘
                                │
                       ┌────────▼────────┐
                       │  Cloud Service  │
                       │  - Receives chat messages       │
                       │  - Sends to Clients queue       │
                       │    without ClientId             │
                       └─────────────────┘
```

**Message Flow:**
1. Client sends `ChatRequest` to Cloud queue
2. Cloud service receives request with authenticated tenant/client identity
3. Cloud sends **ONE** `ChatResponse` to Clients queue with `TenantId` and `Topic: "chat"` (no `ClientId`)
4. Worker detects topic broadcast mode (has `Topic` header)
5. Worker stores message in Azure Table Storage with 5-minute expiration
6. Worker schedules cleanup via Storage Queue (visibility timeout: 5 min)
7. Worker notifies SignalR group: `topic/{tenantId}/chat`
8. Active clients receive `BroadcastAvailable` notification via SignalR
9. Clients request the broadcast message via `RequestBroadcast(messageId)`
10. Worker creates Storage Queue entry for each requesting client
11. Clients poll their queues and display the message (existing mechanism)
12. After 5 minutes, cleanup function deletes broadcast from Table Storage

**Topic-Based Broadcast Architecture:**
- **Reactive pull pattern**: Only active clients receive messages (not stored for offline clients)
- **SignalR groups**: Clients subscribe to topics (e.g., "chat") to receive notifications
- **Shared storage**: Azure Table Storage for broadcasts, Storage Queue for cleanup
- **Multi-instance safe**: No in-memory state, supports multiple worker instances
- **Tenant isolation**: Topic groups include tenantId: `topic/{tenantId}/{topic}`
- **Automatic cleanup**: Storage Queue visibility timeout triggers cleanup after expiration

## Multi-Tenant Security

Each client authenticates with:
- **Client Assertion JWT** (signed with client's private key)
- **Tenant-specific token endpoint**: `/api/{tenantId}/token`
- **Standards-compliant OAuth 2.0 flow** (RFC 7523)

The dbote Worker:
- Validates all JWTs against the identity provider's JWKS
- Injects authenticated tenant/client identity into messages
- **Prevents header injection attacks** (clients cannot spoof identity)
- **Routes topic broadcasts** via SignalR groups and Table Storage
- Uses reactive pull pattern: only active clients receive broadcasts
- Routes messages only within tenant boundaries

## Cleanup

```bash
# Stop and remove containers
docker compose down

# Remove volumes (resets all data)
docker compose down -v
```

## Troubleshooting

**Clients fail to start with "Authority is not configured"**
- The docker-compose.yaml should have all required configuration
- For local dev, ensure appsettings.Development.json has Authority, TokenEndpoint, and Scope

**Messages not being delivered**
- Check all services are healthy: `docker compose ps`
- Check client logs: `docker compose logs tenant-a-client-a`
- Verify clients are on the same tenant to communicate

**Port already in use**
- Change ports in docker-compose.yaml if 5000-5002 are occupied
