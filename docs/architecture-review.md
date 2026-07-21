# Architektur-Review — Übersicht & Referenz

> Arbeitsdokument. Stabile IDs (H/M/N) sind das gemeinsame Vokabular: wenn wir später „H1" sagen, ist **dieser** Punkt gemeint.
> Erstellt aus der Architektur-Bewertung vom 2026-07-21. Fokus: Ecken/Kanten der Anwendungs­architektur (kein Code-Review).

## Legende

- **Schwere:** 🔴 Hoch · 🟡 Mittel · ⚪ Niedrig
- **Status:** `offen` · `in Klärung` (braucht Design-Entscheidung von euch) · `in Arbeit` · `erledigt` · `verworfen` (bewusst akzeptiert)

## Master-Tabelle

| ID | Titel | Schwere | Bereich / Anker | Kern in einem Satz | Status |
|----|-------|:---:|-----------------|--------------------|--------|
| **H1** | Rückkanal nicht durabel | 🔴 | Client-Transport / SignalR (`SignalRClient.cs`, `BoteTransport.OnCommit`, `BoteHub.Messages.cs`) | Client→Cloud sendet fire-and-forget über SignalR (`SendAsync`) → at-most-once auf dem kritischsten Pfad. | erledigt (A: `InvokeAsync`+Propagation, live verifiziert; Dedup-Enabler `MessageId` + `RequiresDuplicateDetection`; Offline-Durabilität = App-Outbox) |
| **H2** | Client-Dead-Letter läuft ins Leere | 🔴 | Worker-Whitelist / Client-Retry (`BoteHub.Messages.cs`) | Error-Queue wird per SignalR abgelehnt (nicht in Whitelist); wegen H1 unbemerkt → stiller Verlust. | erledigt (A: lokale Edge-Error-Queue via eigenem `IErrorHandler` statt SendMessage-Whitelist; Worker stellt Write-SAS für `{prefix}-{tenant}-{client}-error` aus; 3 Unit-Tests grün; Live-Poison-Pfad E2E verifiziert nach H3-Fix: Redelivery→Dead-Letter→lokale Error-Queue mit `rbs2-error-details`) |
| **H3** | SignalR = SPOF ohne Recovery | 🔴 | Client (`SignalRClient.cs`, `PendingMessagesIndicator.cs`, `BoteHub.cs`) | Kein Auto-Reconnect, 15-min-Token trennt planmäßig, Indicator startet `false` → wartende Nachrichten nach Reconnect nie gepollt. | erledigt (a) `WithAutomaticReconnect`+`Reconnected`/`Closed`-Handler (unbegrenzte Policy); (b) Indicator-Arm bei Kaltstart/Reconnect + periodischer Reconcile-Backstop (`IInitializable`, Default 60 s); (c) OnNack re-armt Indicator → Redelivery; live E2E: Redelivery→Dead-Letter→lokale Error-Queue) |
| **H4** | DataBus-Choreographie fragil & ≠ Doku | 🔴 | Worker/Pipeline (`DataBusOutgoingStep.cs`, `BoteHub.DataBus.cs`, `DataBusCopyProcessor.cs`) | 5 entkoppelte Trigger ohne Klammer; Fehler geschluckt; Cleanup-Timer & Statusmechanik der Doku fehlen; keine Idempotenz. | erledigt (a) `DataBusOutgoingStep` lädt synchron & propagiert Upload-Fehler (kein detached `Task.Run`/5-s-Race/Swallow mehr) — 2 Azurite-Tests; (b) Copy-Idempotenz via `IfNoneMatch=*` auf `StartCopyFromUri`; (c) Cleanup-Modell = beschränktes Blob-TTL (Infra-Lifecycle-Policy, 7 T) statt unsicherem Inline-Delete — siehe Detail) |
| **H5** | Header-Blockliste + langlebige SAS in Headern | 🔴 | Trust-Boundary (`BoteHub.Messages.cs`, `BoteHub.DataBus.cs`, `DataBusIncomingStep.cs`) | Nur 2 Header geblockt (Blockliste statt Allowlist); Cloud lädt blind von Header-URLs; 30-Tage-`Read\|Delete`-SAS in ASB-Headern. | offen |
| **H6** | Geteilte `bote-clients`-Queue ohne Kompensation | 🔴 | Worker (`BoteHub.Messages.cs`, `BoteHub.Subscriptions.cs`) | Sequenzieller Broadcast-Fan-out (Head-of-Line, Duplikate), Reschedule multipliziert Volumen, mandantenübergreifende DLQ, kein Throttling. | offen |
| **H7** | Kein Test-Harness | 🔴 | `src/workers/tests/`, `test/` | Leerer Stub + 1 KeyGenerator-Test; keine Integrationstests für Header-Injektion, Reschedule, Copy-Statemachine, Isolation. | offen |
| **M1** | Config-Drift Infra/Dev/Code/Doku | 🟡 | `infra/main.ts`, `dev/config/servicebus.json`, `samples/*`, `claude.md`, `docs/databus-concept.md` | ASB Basic vs. `ScheduleMessageAsync` (braucht Standard); fehlende `cloud-incoming`-Queue; Queue-Namens- & Options-Drift; veraltete Doku. | offen |
| **M2** | Kopplung an Emulator- & Rebus-Interna | 🟡 | `dev/asbapi/`, `docker-compose.yaml`, `Rebus.Integration` | Geteilter `servicebus`-Alias + Management-API-Fake; Pipeline-Step-Positionen; SDK-Update kann Dev+Integration gleichzeitig brechen. | offen |
| **M3** | Nachrichtengrößen-Bruch zwischen Transporten | 🟡 | `Converters/MessageConverter.cs`, `BoteHub.Messages.cs` | ASB 256 KB / Storage-Queue ~64 KB / SignalR-Limits / JSON-Overhead — nirgends validiert, kein definiertes Fehlerbild. | offen |
| **M4** | Tenant-Level-Attachmentzugriff + Delete-SAS | 🟡 | `BoteHub.DataBus.cs`, `TenantStorageResolver.cs` | Jeder Client eines Tenants kann alle Attachments lesen/löschen/überschreiben; Isolation nur Pfad+GUID; Account-Key-SAS schließt Managed Identity aus. | offen |
| **M5** | IdP-Kopplung & Ausfallverhalten | 🟡 | `TokenValidationService.cs`, `BoteHub.cs` | Kein Refresh-on-unknown-`kid`, kein Stale-Cache-Fallback; 15-min-Token macht IdP zur harten Laufzeitabhängigkeit. | offen |
| **M6** | Observability endet an Prozessgrenzen | 🟡 | systemweit | Keine Trace-Korrelation über ~7 Hops + async Copy; Worker→AppInsights, Rest nur Console. | offen |
| **M7** | Dead Code verwässert Grenzen | 🟡 | `src/client/src/Dbosoft.Bote.Client.Core/`, `BoteHub.Messages.cs` | `Client.Core/Abstractions/Primitives` in keiner Solution (alter AMQP-Ansatz); `BoteHeaders.Signature` ungenutzt; tote Whitelist-Logik. | offen |
| **N1** | Secrets-Hygiene in Samples | ⚪ | `samples/*/docker-compose.yaml`, `local.settings.json` | Klartext-Keys/Master-Secret in der Kopiervorlage → schlechtes De-facto-Muster. | offen |
| **N2** | Resolver ohne Client-Caching | ⚪ | `TenantStorageResolver.cs` | Neue `BlobServiceClient`/`QueueClient` pro Aufruf — bei 1000 Tenants vermeidbarer Kostenpunkt. | offen |
| **N3** | Betriebsreste | ⚪ | `samples/simple/NUL`, `BoteTransport`, diverse TODOs | Windows-Artefakt, auskommentierte Fehlerbehandlung, sicherheitskritische TODOs. | offen |

## Übergreifende Muster

1. **„Push als Optimierung" → „Push als Abhängigkeit":** das durabelste Element (HTTPS-Queue) ist für den halben Fluss abgeschaltet. (betrifft H1, H3, H4)
2. **Doku = Ziel-, Code = Ist-Architektur, ohne Kennzeichnung.** (betrifft H4, M1)
3. **Sicherheitsinvarianten verstreut statt zentral** (Blockliste-Symptom). (betrifft H5, M4)
4. **Namens-/Config-Konventionen dreifach dupliziert** ohne Prüfmechanismus. (betrifft M1, H4)
5. **Reliability-Muster „ein Level zu flach"** (Retry ohne E2E-Quittung, Reschedule durch geteilte Queue, DLQ ohne Mandantenfähigkeit, Cleanup nur dokumentiert). (betrifft H1, H2, H4, H6)

## Detail je Punkt (Kurzkontext für die Durchsprache)

### H1 — Rückkanal nicht durabel
**Was:** Einziger Sendepfad des Clients ist `SignalRClient.SendMessage` → `HubConnection.SendAsync` (bewusst nicht `InvokeAsync`). Rebus-`OnCommit` gilt als erfolgreich, sobald der Frame das Socket verlässt.
**Warum:** Worker-Fehler (Whitelist-Verstoß, Konvertierung, ASB down) bleiben für den Client unsichtbar → Verlust im Normalbetrieb erreichbar (Reconnect-Fenster, Throttling, Hub-Exceptions). Kollidiert mit „No message loss".
**Kern-Entscheidung:** at-most-once bewusst? Sonst: Storage-Queue+Queue-Trigger upstream (wie Doku), SignalR nur Notify.

### H2 — Client-Dead-Letter läuft ins Leere
**Was:** Rebus verschiebt Giftnachrichten in `…-error` — wieder per SignalR. Whitelist erlaubt nur `Cloud`/`Clients` → `UnauthorizedAccessException`. Wegen H1 unbemerkt, Rebus ACKt → Nachricht weg.
**Warum:** Der Client-seitige Dead-Letter-Pfad ist nicht zu Ende designt; stiller Verlust ist die Default-Konsequenz.

### H3 — SignalR = SPOF ohne Recovery
**Was:** HubConnection ohne `WithAutomaticReconnect`/Closed-Handler (`// TODO`); `CloseOnAuthenticationExpiration=true` + 15-min-Token → planmäßige Trennung. `PendingMessagesIndicator` startet `false`, wird nur per Push gesetzt.
**Warum:** Nach Reconnect/Kaltstart pollt der Client wartende Nachrichten nie; nach ≤15 min wird ein Client stumm. Offline ist bei Edge der Regelfall.

### H4 — DataBus-Choreographie fragil & ≠ Doku
**Was:** Fluss hängt an Pipeline-Step + Blob-Trigger + Copy-Monitor-Queue + ASB-Reschedule + SignalR-Notify, koordiniert nur über Blob-Existenz + `CopyStatus`. `DataBusOutgoingStep` lädt detached & schluckt Fehler. Dokumentierte Cleanup-Timer & `pending→complete`-Statusmechanik **nicht implementiert** → verwaiste Blobs. `Failed`-Zweige ohne Abbruch. Keine Idempotenz/Dedup.
**Warum:** „Nie durchgelaufen + keine Tests + viele async Teilnehmer" = Blindflug bei jeder Änderung.
**Fix (erledigt):**
- **H4a** — `DataBusOutgoingStep` lädt das Attachment jetzt **synchron** in die Outbox und lässt einen Upload-Fehler propagieren (der Send scheitert → Rebus retryt), statt via `Task.Run` + `WhenAny(…, Delay(5000))` + `catch`-Log die Nachricht ohne Blob loszuschicken. 2 Azurite-Integrationstests (`DataBusOutgoingStepTests`).
- **H4b** — `DataBusCopyProcessor` startet den Copy mit `DestinationConditions IfNoneMatch=*`; nebenläufige/wiederholte Trigger (Blob-Trigger ist at-least-once) bekommen 409 und überwachen den laufenden Copy statt doppelt zu kopieren.
- **H4c (Cleanup-Modell)** — Inline-Delete ist hier **unsicher**: ein retryte Nachricht muss ihr Attachment erneut lesen, und Broadcast-Blobs liegen pro Tenant unter `{tenantId}/{attachmentId}` und werden von allen Subscribern geteilt (M4) → kein einzelner Client/Handler darf löschen. Retry-sicherer Backstop = **beschränktes Blob-TTL** (Azure-Lifecycle-Policy, 7 Tage, in `infra/main.ts` auf dem DataBus-Storage-Account; nur Blobs betroffen, Queues/Table nicht). Erfolgspfade löschen ihre Quell-Blobs weiterhin sofort. (Ersetzt die in der Doku behauptete, nie implementierte `pending→complete`+Timer-Mechanik. Infra-Policy von Azurite nicht testbar; TTL-Fenster = Designparameter.)

### H5 — Header-Blockliste + langlebige SAS in Headern
**Was:** `SendMessage` blockt nur `TenantId`/`ClientId`. Neuere Header (`…-sas`, `attachment-id`, `Topic`) nicht → spoofbar. Cloud-`DataBusIncomingStep` lädt blind von jeder Header-URL (SSRF-Fläche). Worker stellt 30-Tage-SAS `Read|Delete` aus, transportiert in ASB-Headern → DLQ/Telemetrie.
**Warum:** Trust-Boundary ist das Wertversprechen; Blockliste skaliert nicht mit Features; langlebige Delete-Capabilities in persistierten Nachrichten = großes Fenster.

### H6 — Geteilte `bote-clients`-Queue ohne Kompensation
**Was:** Single-Queue-Entscheidung ist dokumentiert & sinnvoll (ASB-Trigger-Autoscaling). Unadressiert: sequenzieller Broadcast-Fan-out (Head-of-Line über Tenants; Duplikate bei Teilfehler), Reschedule-Schleife multipliziert Volumen, mandantenübergreifende DLQ ohne Tooling, kein Fairness/Throttling.
**Warum:** Bei „1000+ Tenants" ist die geteilte Queue der Punkt, an dem sich Mandanten gegenseitig beeinflussen.

### H7 — Kein Test-Harness
**Was:** `UnitTest1.cs` leer; nur KeyGenerator-Test. Keine Integrations-/Contract-Tests für Header-Injektion, Reschedule, Copy-Statemachine, Transport-Commit, Isolation.
**Warum:** Architektur = fast nur async Koordination + Sicherheitsinvarianten; ohne Tests kein Sicherheitsnetz (der DataBus-Bruch blieb unbemerkt).

### M1 — Config-Drift Infra/Dev/Code/Doku
ASB **Basic** (infra) vs. `ScheduleMessageAsync` (braucht Standard); `cloud-incoming`-Queue fehlt in infra; Sample-Queues `bote-sample-simple-*` vs. Emulator `bote-dev-*`; `Events` vs. `CloudIncoming`; veraltete CLAUDE.md/Doku.

### M2 — Kopplung an Emulator- & Rebus-Interna
Geteilter `servicebus`-Alias + ATOM-XML-Management-Fake (nur `GET`); Abhängigkeit von Rebus-ASB-Interna (Management-API-Nutzung, `INameFormatter`-Decorator, Step-Positionen). SDK-Update kann beides gleichzeitig brechen.

### M3 — Nachrichtengrößen-Bruch zwischen Transporten
ASB 256 KB / Storage-Queue ~64 KB / SignalR-Limits / JSON-String-Overhead. Cloud-Nachricht >~48 KB scheitert erst bei `ForwardToClientQueue`; keine Validierung, kein definiertes Fehlerbild; 150-KB-Embedded-Tier der Doku im Code nicht auffindbar.

### M4 — Tenant-Level-Attachmentzugriff + Delete-SAS
Attachments auf `{tenantId}/{attachmentId}` (Broadcast-Support). Jeder Client des Tenants kann lesen/**löschen**/überschreiben; Lese-Validierung prüft nur Blob-Metadata `tenantid`; Upload-SAS erlaubt Überschreiben mit beliebiger Metadata. `GenerateSasUri` erzwingt Account-Key → Managed Identity/User-Delegation-SAS nicht nutzbar.

### M5 — IdP-Kopplung & Ausfallverhalten
JWKS 1 h gecacht, kein Refresh-on-unknown-`kid` (Rotation in der Stunde = Login-Totalausfall), kein Stale-Cache-Fallback bei IdP-down. 15-min-Token + Close → IdP ist harte Laufzeitabhängigkeit jedes Clients im 15-min-Takt (konträr zu „Edge autark").

### M6 — Observability endet an Prozessgrenzen
Keine durchgängige Korrelation (kein traceparent über SignalR→ASB→Storage-Queue→Blob-Copy); Worker→AppInsights, Cloud/Client nur Console. ~7 Hops + async Copy → „kam nie an" nur per Handarbeit über MessageIds in 3 Systemen diagnostizierbar.

### M7 — Dead Code verwässert Grenzen
`Client.Core` (~80 KB AMQP-Konverter, `BoteClient`, Batches) + `Client.Abstractions`/`Client.Primitives` in keiner Solution referenziert. `BoteHeaders.Signature` ungenutzt. `SendMessage`-Whitelist erlaubt `Clients`, obwohl Code danach immer an `Cloud`/`CloudIncoming` sendet (tote/halbfertige Logik).

### N1 — Secrets-Hygiene in Samples
Private ECDSA-Keys, Access-Keys, Master-Function-Secret im Klartext in Sample-Compose/`local.settings.json`. Dev-only, aber Samples sind die Kopiervorlage → „Signing-Key als Env-Var" wird De-facto-Standard (README nennt gerade Keystore-Schutz als Motiv).

### N2 — Resolver ohne Client-Caching
`DefaultTenantStorageResolver` erzeugt pro Aufruf neue `BlobServiceClient`/`QueueClient`. Guter Abstraktionspunkt (per-Tenant/geo-Storage), aber ohne Caching/Lebenszyklus — bei 1000 Tenants + hoher Rate vermeidbarer Kostenpunkt.

### N3 — Betriebsreste
`samples/simple/NUL` (Windows-Artefakt), auskommentierte `throw new RebusApplicationException` im Transport, sicherheitskritische TODOs (`// TODO review`, `// TODO test reauthentication`, `// TODO filter headers`).

## Offene Design-Fragen (Sammlung)

1. Delivery-Garantie Client→Cloud (bewusst at-most-once?) — betrifft **H1**
2. Ziel für Client-Poison-Messages — betrifft **H2**
3. Reconnect-/Offline-Nachholstrategie — betrifft **H3**
4. Broadcast-Persistenz vs. „Offline verliert" (Code ≠ Doku) — betrifft **H6**
5. Skalengrenze geteilte Queue + Sharding-Plan — betrifft **H6**
6. SAS-Politik (30 Tage Delete? Managed Identity?) — betrifft **H5**, **M4**
7. Attachment-Lebenszyklus/Cleanup — betrifft **H4**, **M4**
8. Versionierter Header-Allowlist-Kontrakt — betrifft **H5**
9. E2E-Harness als CI-Gate — betrifft **H7**
10. ASB Standard-SKU festschreiben — betrifft **M1**
