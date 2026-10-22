# Test harness

Two tiers of tests guard the messaging gateway. Run everything with `dotnet test` from the
repository root, or a single project as noted below.

## Fast unit tests (no external services)

- **`Dbosoft.Bote.Rebus.Tests`** — the client transport contract:
  - `BoteTransportSendTests` — the send commit propagates failures and carries the message id.
  - `BoteErrorHandlerTests` — poison messages go to the local edge error queue with error details.
- **`Dbosoft.Bote.Client.KeyGenerator.Tests`** — ECDSA key round-trip.
- Pure-logic tests that live in the integration project but touch no container:
  - `BoteHeaderPolicyTests` — the trust-boundary header allow-list.
  - `DataBusSasUriValidatorTests` — the SSRF host check on inbox SAS URLs.
  - `BroadcastFanOutTests` — bounded-parallel, failure-isolating broadcast fan-out.

## Integration tests (real Azure Storage via Testcontainers/Azurite)

**`Dbosoft.Bote.Rebus.IntegrationTests`** starts a real Azurite container (shared across the
storage-backed classes via the `Azurite` collection fixture):

- `PoisonRedeliveryEndToEndTests` — a failing handler is redelivered and then dead-lettered to
  the client's local error queue (end to end through the real transport and Rebus).
- `DataBusOutgoingStepTests` — the cloud stages an attachment in the outbox before forwarding,
  and propagates upload failures.
- `DataBusCopyProcessorTests` — the copy state machine's idempotency short-circuit and
  fail-fast. (The server-side copy-from-URL itself is covered by the live emulator
  stack, where the worker and storage share a network; Azurite in a container can't reach a
  source SAS on the test host's mapped port.)

### Running the integration tests

They need a Docker-compatible container runtime. With **Podman** (the local dev setup), set
before running:

```
DOCKER_HOST=npipe://./pipe/podman-machine-default   # Windows named pipe
TESTCONTAINERS_RYUK_DISABLED=true
```

CI with Docker needs neither. The Azurite image is pinned to `mcr.microsoft.com/azure-storage/azurite:3.35.0`
(the same version the dev compose stack uses) so its queue API version matches the SDK.
