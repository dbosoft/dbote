# Load-test sample — mass-scalable on-prem integration

Simulates the pattern of a central, horizontally scalable processing unit (the **Controller**)
talking to **hundreds of edge systems** (the on-prem "connectors"), all through the Bote gateway
and the Rebus + Azure Service Bus architecture — the same shape as an SAP integration hub.

## Shape

- **Controller** (`Dbosoft.Bote.Samples.LoadTest.Controller`) — cloud side: Rebus + Azure Service
  Bus + `EnableBote`/`EnableBoteDataBus`. Handles edge registration and small/large requests,
  replies **targeted** to the sending edge, and periodically **broadcasts** a short notification.
  Headless message handler: scale by running multiple instances against the shared queue. Each
  replica pushes its handling metrics out-of-band to the consolidator (`LoadTest:ConsolidatorUrl`).
- **EdgeLoad** (`Dbosoft.Bote.Samples.LoadTest.EdgeLoad`) — the load generator: a single process
  that spins up **N independent Rebus buses** (a "Rebus holder"), each an edge client over the
  Bote transport (own SignalR connection + storage queue). Each edge registers dynamically with
  the identity provider, then exchanges small messages (a few KB) frequently and large messages
  (a few MB, via DataBus attachments) occasionally, and receives targeted responses + broadcasts.
- **Dashboard** (`Dbosoft.Bote.Samples.LoadTest.Dashboard`) — a separate consolidator web app that
  both the controller replicas and the edge generators push their metrics to, so it can sum across
  a scaled-out deployment (distinct controller replica count = the scale signal). Used in the Azure
  deployment (`infra/`); **not wired into the local compose stack** — see below.
- Small = inline payload; large = DataBus (claim-check). Broadcasts are used only for lightweight
  notifications, never for bulk data.
- **Noisy-neighbor scenario** — the first `LoadTest:NoisyEdges` edges flood their queue at
  `LoadTest:NoisyIntervalMs` (small messages only) while the rest stay well-behaved. A reference
  per-client **token-bucket dampener** in the controller (`ClientFairness`,
  `LoadTest:Fairness:Enabled/RatePerSec/Burst`) defers over-budget clients via Rebus
  `TransportMessage.Defer` so one flooder cannot monopolise the shared queue.

## Run locally (Podman)

```
./Start-LoadTest.ps1            # 30 edges (default)
./Start-LoadTest.ps1 -Edges 100 # simulate 100 edge systems
```

The consolidator **Dashboard is not part of the local compose stack** (it targets the scaled
Azure deployment). Locally, read the metrics straight from the container logs:

```
podman compose logs -f edgeload      # round-trip throughput + latency, quiet vs noisy p95
podman compose logs -f controller    # handling throughput; ClientFairness admitted/deferred
```

Tunables (env): on `edgeload` — `LoadTest__EdgeCount`, `LoadTest__SmallIntervalMs`,
`LoadTest__SmallBytes`, `LoadTest__LargeEveryN`, `LoadTest__LargeBytes`, `LoadTest__StartupStaggerMs`,
`LoadTest__NoisyEdges`, `LoadTest__NoisyIntervalMs`; on `controller` —
`LoadTest__Fairness__Enabled`, `LoadTest__Fairness__RatePerSec`, `LoadTest__Fairness__Burst`.

Each simulated edge is a full Rebus bus (its own connection + queue + worker loop), so ~100 in one
process is comfortable; for many more, run several `edgeload` containers or split across hosts.

## Azure

The same code runs against real Azure by pointing the connection strings / endpoints at Azure
Service Bus, Storage, SignalR and a real identity provider (config only). Scaling model:
- **Controller** → multiple instances / Container Apps replicas on the shared Service Bus queue.
- **EdgeLoad** → run as container replicas (e.g. Container Instances / a job) to reach thousands
  of edges; each replica hosts a slice of the edge population.
- Per-tenant fairness/sharding at that scale is an application concern the Service Bus choice
  enables (sessions / hash queues / dedicated queues).
