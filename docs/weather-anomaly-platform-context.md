# Weather Anomaly Alert Platform — Architecture & Design Context

This document captures the full design discussion for a C#/.NET 10 weather anomaly
alert platform. It exists so another AI agent (or the same one, later) can pick up
implementation work with full context, without re-deriving decisions already made.

No implementation exists yet. Everything below is conceptual/architectural.

---

## 1. Purpose

Alert the user during the day when weather conditions change unexpectedly from what
the morning forecast promised — e.g., forecast says fair weather in the morning, but
a storm/flood/high-wind event develops later. The point is to catch **sudden,
unforecast changes**, not to replace a normal weather app.

Phase 1 scope is deliberately narrow:
- Hazards: storms, high winds, floods only (extensible later).
- Data sources: open weather APIs only (extensible later to specialized/hyperlocal
  APIs and home sensors, e.g. Google Home).
- Delivery: ntfy on mobile only (extensible later to a custom app + public API).
- Users: predefined public locations only, no user accounts yet (a "couple dozen
  users" scale target).

## 2. Technology stack (as decided)

- C#, .NET 10
- Microservices with Domain-Driven Design (DDD)
- MassTransit as messaging abstraction (transport-agnostic: RabbitMQ locally,
  Azure Service Bus in Azure)
- Docker (local dev, containers everywhere)
- .NET Aspire (local orchestration/dev only — not a production concern)
- PostgreSQL + TimescaleDB, EF Core
- xUnit (+ Testcontainers for integration tests)
- OpenTelemetry (trace correlation across the whole pipeline)
- GitHub Actions (CI/CD)
- Azure as primary cloud target, but **explicit goal: stay cloud-agnostic** —
  every infrastructure dependency (broker, DB host, scheduler trigger, secrets
  manager) must be swappable via configuration, not hardcoded to Azure.

## 3. Microservices / Bounded Contexts

Four services in phase 1. A fifth (Subscriber Registry) was considered and
explicitly **dropped** — see §3.5.

### 3.1 Ingestion Service — bounded context: "Weather Data Acquisition"

**Purpose:** Acquire weather facts (predicted and actual) from any number of
heterogeneous external sources, normalize into one canonical vocabulary, and store
authoritative history. This is currently the **active focus of implementation
discussion**.

**Ubiquitous language:**
- **Provider** — an external source (OpenWeatherMap, Tomorrow.io, a Google Home
  device, a personal weather station). Every fact is labeled with its ProviderId —
  **this is a hard requirement**, not optional metadata (see §3.1.5).
- **Forecast Snapshot** — a predicted state of weather variables for a future
  *Target Time*, as of a specific *Issued At* time.
- **Observation Reading** — an actually measured state of weather variables at a
  specific *Observed At* time — ground truth, not a prediction.
- **Weather Variable** (value object) — typed measurement: WindSpeed,
  PrecipitationIntensity, BarometricPressure, FloodRiskIndex, etc., value + unit.
- **Source Adapter** — the per-provider translation component. Every new provider
  (weather API or sensor) is a new adapter; nothing downstream should ever know
  provider-specific shapes.

**Aggregates:**
- `ForecastSnapshot` — Id, LocationId, ProviderId, IssuedAt, TargetTime, weather
  variable readings. Invariant: TargetTime ≥ IssuedAt.
- `ObservationReading` — Id, LocationId, ProviderId/SensorId, ObservedAt, weather
  variable readings. Invariant: ObservedAt cannot be in the future (small
  clock-skew tolerance allowed).

**Events published:** `ForecastSnapshotIngested`, `ObservationRecorded`.

**Storage:** Both persisted long-term to TimescaleDB hypertables, partitioned by
location + time. This is the authoritative historical archive for the whole
platform (Detection keeps only a small rolling working set, not full history).

#### 3.1.1 Why Observations were added (not just Forecasts)

Original design only had Forecast Snapshots (predictions). User explicitly wanted
Detection to compare **both** forecasts-vs-forecasts AND live readings, to catch
sudden onset faster than waiting for a provider to update its forecast. This is why
Ingestion acquires two distinct fact types (predictions vs. ground truth), not one.

#### 3.1.2 Google Home / sensor integration — same service or new one?

Decision: **keep as an adapter inside Ingestion for now**, not a separate service.
Reasoning: reason-to-change test — a sensor adapter is structurally the same job as
a weather-API adapter (call external source, normalize, publish). However:
- Model it as a **distinct domain concept**, `ObservationReading` (not
  `ForecastSnapshot`), from day one — observations and forecasts are semantically
  different (actual vs. predicted), even if handled by the same service.
- If sensor integration later grows real distinct complexity (OAuth device linking,
  per-user device management, different failure/auth model), that's the point
  where splitting into a separate "Sensor Integration" service becomes justified.
  Not needed yet.

#### 3.1.3 Scheduler / Worker split (horizontal scaling design)

Ingestion is **not** request/response — it's a scheduled job dispatcher — so naive
"add more replicas" breaks it (duplicate polling, duplicate events) unless
scheduling is decoupled from execution.

**Structure decided:**
- One bounded context (Ingestion), delivered as **multiple deployable
  processes/container images**, all referencing a shared domain/application/
  infrastructure class library. This is legitimate DDD — a bounded context isn't
  required to be one process, just one cohesive domain model + data ownership.
- `Ingestion.Scheduling` (shared library) — contains `PollDispatchService`: given
  "now," asks Location Catalog for active locations, checks each
  (Location, Provider) pair's last-poll timestamp against configured cadence,
  enqueues `PollLocationRequested` for anything due. **Must be idempotent /
  catch-up tolerant** — calling it early is a harmless no-op, calling it late just
  means slightly delayed polling. This tolerance is what makes the trigger
  mechanism swappable (see below) — if due-checking assumed precise calling
  cadence, the trigger choice would matter far more.
- **Thin, environment-specific trigger hosts** invoke `PollDispatchService.RunOnce()`
  on whatever schedule that environment provides, and do nothing else:
  - Azure: an **Azure Container Apps Job with a cron trigger** (chosen over a
    persistent Quartz.NET process specifically to stay in ACA's free/no-idle-cost
    model — see §5).
  - Local / self-hosted / anywhere else: a small Worker Service with an in-process
    timer, Quartz.NET, or a `PeriodicTimer` — user's choice per deployment.
  - Any other environment: same idea — external trigger (systemd timer, k8s
    CronJob, scheduled CI run) invoking the same containerized entrypoint once.
  - **Explicit user requirement:** must support a fully custom/self-hosted
    scheduler as an option, chosen at deploy time — not hardcoded to ACA Jobs.
- `Ingestion.Worker` — separate executable/container, a RabbitMQ/Service Bus
  consumer (via MassTransit) that picks up `PollLocationRequested`, calls the
  correct provider adapter (looked up by ProviderId), normalizes, persists,
  publishes the result event via the transactional outbox (see §3.1.4).
  - One shared worker binary, parameterized by config (e.g. `QUEUE_NAME` env var)
    for which queue(s) it listens to — avoids maintaining N separate images per
    provider while still allowing independent replica scaling per queue.
  - Scales horizontally via competing-consumer pattern on the broker — this is
    the layer that actually needs more replicas, not the scheduler.
  - On Azure: scales via **KEDA** (queue-length-based autoscaling in Container
    Apps), including scale-to-zero when idle.

**An earlier idea — a "self-perpetuating scheduled message chain" where each
worker enqueues its own successor using Service Bus's native scheduled-message
feature — was considered and explicitly set aside** in favor of the ACA
Job/cron + thin-host approach above, because:
- It leans on a Service Bus-specific feature (scheduled enqueue time), working
  against the cloud-agnostic/broker-swappable goal (RabbitMQ has no equivalent
  without extra plugins).
- It has a real failure mode: if a worker crashes after processing but before
  scheduling its successor, that location's polling chain silently dies with no
  self-correction.
- The plain cron-triggered dispatch approach is more robust and equally free-tier
  friendly (ACA Jobs bill only for actual run duration, not idle time between
  runs).
- Worth remembering as a discussed-and-rejected alternative, not to resurrect
  without addressing the chain-breakage risk (a reconciliation/health-check job
  was the proposed mitigation, if ever revisited).

**Quartz.NET** was approved as one valid local/self-hosted scheduler option
(clustered mode with an ADO.NET job store in Postgres would give HA if ever
needed), but is not the primary Azure-hosted approach given the ACA Jobs decision
above.

#### 3.1.4 Outbox pattern

**Decided: use MassTransit's built-in transactional outbox**
(`AddEntityFrameworkOutbox`), not a hand-rolled outbox table + custom relay.

Reasoning: solves the classic dual-write problem (Worker must persist a snapshot
to Postgres AND publish an event as one atomic unit — partial failure either way
is unacceptable). MassTransit's outbox manages the outbox table via the existing
EF Core `DbContext` and handles the publish-and-mark-sent relay internally. Chosen
over hand-rolling because it integrates cleanly with the already-decided transport
abstraction (works the same regardless of whether the underlying broker is
RabbitMQ or Service Bus) and is less code to own, at the cost of it being "someone
else's magic" to be able to explain if asked (acceptable trade-off given the
project's dual goal of shipping something real + being interview-explainable).

#### 3.1.5 Provider labeling — important correction to the model

User's own insight, explicitly incorporated: **every fact must be labeled with its
ProviderId as a load-bearing field, not incidental metadata.** Different providers
disagree (methodology, model resolution, cadence) — treating "wind speed" as one
universal number regardless of source risks Detection firing false anomalies that
are really just inter-provider noise, not real hazard signals.

Consequences for the model (carried into Detection, §3.2):
- Detection's `Baseline` is keyed by **(LocationId, ProviderId, TargetTime)**, not
  just (LocationId, TargetTime).
- "Reality Divergence" (forecast vs. observation) should by default compare a
  provider's own forecast against its own observation — not cross-provider —
  since crossing sources conflates "the world changed" with "these sources merely
  disagree."
- Cross-provider corroboration ("3 of 4 providers now agree") is a legitimate
  future capability but must be modeled as an **explicit, separate concept** (e.g.
  a `ConsensusEvaluation`), not an implicit average across sources. Phase 2 idea,
  not phase 1.

#### 3.1.6 Idempotency

RabbitMQ/Service Bus both give at-least-once delivery, so duplicate processing of
the same `PollLocationRequested` job is possible. Mitigation: a **unique
constraint** on (LocationId, ProviderId, IssuedAt) for snapshots and
(LocationId, ProviderId/SensorId, ObservedAt) for observations — duplicate writes
become a no-op (upsert or ignore constraint violation) rather than corrupting
history.

#### 3.1.7 Resilience around external provider calls

**Decided: Polly** for retry/backoff policies. **Decided: providers must fail
independently per location/provider** — one failing provider must never poison an
entire poll cycle for a location or block other providers. Specific retry
policies, transient-vs-permanent-failure classification, and dead-lettering
behavior were explicitly deferred as implementation-time detail.

#### 3.1.8 Layering (applies to all services, detailed here as the current focus)

Standard onion/clean architecture per service:
- **Domain** — entities, value objects, domain events, strategy objects
  (`HazardRule` in Detection), repository *interfaces* only. Zero dependencies
  beyond BCL.
- **Application** — use case handlers, DTOs, validation, orchestration of domain +
  ports. MassTransit consumers live here as thin adapters calling into
  application handlers, not fat consumers with embedded logic.
- **Infrastructure** — EF Core repos/DbContext, RabbitMQ/Service Bus (via
  MassTransit) config, external HTTP clients (weather providers), ntfy client,
  OpenTelemetry exporters, secrets/config provider wiring.
- **Host** — Program.cs / entrypoint, Aspire AppHost wiring, minimal API
  endpoints if any. For Ingestion specifically, this layer is where
  Scheduler-host vs. Worker-host vs. local-custom-scheduler-host differ; they all
  reference the same Domain/Application/Infrastructure libraries.

Plus a **shared contracts library** across all services — integration event DTOs
only (`ForecastSnapshotIngested`, `AnomalyDetected`, etc.), versioned, no domain
logic — so services never accidentally couple through shared business logic.

### 3.2 Anomaly Detection Service — bounded context: "Hazard Detection"

**Purpose:** Decide whether conditions have deteriorated enough, for a defined
hazard, to warrant an alert — using three distinct comparison types.

**Ubiquitous language:**
- **Baseline** — smoothed expectation for a (LocationId, ProviderId, TargetTime),
  derived from a **rolling window** (explicitly not a fixed "morning snapshot" —
  user chose rolling window over fixed-per-day baseline) of recent Forecast
  Snapshots for that provider. Recent runs weighted higher as forecasts converge
  toward the target time.
- **Reality Reading** — latest Observation Reading for a location/provider,
  representing current ground truth.
- **Deviation** (value object) — delta between two comparable facts. Three
  flavors, matching three distinct comparison types:
  1. **Forecast Drift** — new ForecastSnapshot vs. Baseline (did the prediction
     itself change?).
  2. **Reality Divergence** — latest Reality Reading vs. Baseline/expectation for
     "now" (is what's happening already diverging from what was predicted?).
  3. **Observation Trend** — a Reality Reading vs. its own recent history (e.g.
     pressure dropping X hPa/hr), **forecast-independent** — catches sudden onset
     fastest since it doesn't wait for a provider to notice.
- **Hazard** — named risk category (Wind, Storm, Flood), extensible.
- **HazardRule** — strategy encapsulating what magnitude/pattern of Deviation
  counts as significant for a given Hazard; declares which Deviation flavor(s) it
  consumes. Combining a Forecast Drift rule and an Observation Trend rule for the
  same hazard (e.g. Wind) was the concrete example discussed.
- **Hysteresis Window** — a Deviation must persist across N consecutive
  evaluations before being promoted to an AnomalyEvent, to avoid flapping on a
  single noisy reading.
- **AnomalyEvent** — "this HazardRule fired for this Location, evidenced by these
  facts."
- **Severity** (value object) — ordinal ranking, feeds Notification priority
  later.

**Aggregates:**
- `Baseline` — keyed by (LocationId, ProviderId, TargetTime bucket), rolling
  window of contributing snapshots, recalculated as new ones arrive, pruned once
  TargetTime passes.
- `AnomalyEvent` — Id, LocationId, HazardType, Severity, DetectedAt,
  EvidenceFactIds (traceability), DeviationDescription. Idempotency key = hash of
  (LocationId, HazardType, triggering fact ids) — handles at-least-once delivery.

**Events consumed:** `ForecastSnapshotIngested`, `ObservationRecorded`.
**Events published:** `AnomalyDetected`.

**Data note:** Detection needs its own small rolling-window store (not
Ingestion's full TimescaleDB archive) — both for the forecast Baseline window and
for a short recent-observation history to compute Observation Trend. Open
question (not yet decided, flagged as revisit-later): store raw recent readings
vs. pre-aggregated incremental stats (e.g. "pressure delta over last hour") —
trade-off between flexibility to add new trend rules later vs. cheaper querying.

### 3.3 Location Catalog Service — bounded context: "Location Reference"

**Purpose:** Single source of truth for which locations exist and how alerts
reach them. Phase-1 simple (predefined locations only — Cluj-Napoca, Brașov,
Oradea, Bucharest, etc. — statically seeded, no user-submitted locations yet),
but modeled so phase-2 (user-submitted locations) slots in without
restructuring.

**Ubiquitous language:**
- **Location** — Id, DisplayName, Coordinates, Region/Country, IsActive (drives
  whether Ingestion polls it).
- **AlertChannel** — the ntfy topic bound to a Location (phase 1: exactly one
  predefined public channel per location).
- *(named now, not built yet)* **Subscriber**, **LocationPreference** — phase-2
  concepts the model should leave room for, not implemented yet.

**Aggregate:** `Location` — Id, Name, Coordinates, AlertChannels, IsActive.

**Events:** `LocationActivated` / `LocationDeactivated` — consumed by Ingestion's
scheduler/dispatch logic to adjust its polling set. Worth having even with a
static seeded catalog, since it makes "add a city" a real domain workflow rather
than a redeploy.

Ingestion polls **only** locations that are active in this catalog — confirmed
requirement (not blanket polling).

### 3.4 Notification Service — bounded context: "Alert Delivery"

**Purpose:** Turn an `AnomalyDetected` event into a delivered, human-readable
message, and know whether it actually went out.

**Ubiquitous language:**
- **Alert** — the rendered, channel-ready message derived from an AnomalyEvent.
  Deliberately does **not** know about Baselines/Deviations — only Hazard,
  Location, Severity, and rendered content (boundary discipline: Notification
  must not inherit Detection's vocabulary).
- **DeliveryChannel** — abstraction over "how": ntfy today, email/push/webhook
  later.
- **DeliveryAttempt** — entity tracking success/failure/retries per channel per
  alert.
- **Template** — how AnomalyEvent data renders into ntfy title/body/priority.

**Aggregate:** `Alert` — Id, SourceAnomalyEventId (trace correlation), LocationId,
HazardType, Severity, RenderedMessage, DeliveryAttempts.

**Events consumed:** `AnomalyDetected`.
**Events published (optional, cheap to add):** `AlertDelivered` /
`AlertDeliveryFailed` — useful audit trail even with no consumer yet.

Resolves LocationId → ntfy topic by querying the Location Catalog (sync call
accepted as the phase-1 approach; a denormalized local read model was discussed
as a possible future optimization if coupling/latency ever becomes a real
problem — explicitly not needed yet at this scale).

### 3.5 Subscriber Registry — considered, then dropped

Originally proposed as a 5th service (dynamic per-user topic generation, opt-in/
opt-out handshake). **Dropped** once the user clarified how ntfy actually works
and how they want phase 1 to behave:

- ntfy has **no accounts, no location awareness, no device registration** — it is
  pure topic-based pub/sub over HTTP. A "topic" is just a string; anyone who knows
  it can subscribe or (without protection) publish.
- Phase-1 decision: **locations are predefined and public**, with a fixed,
  well-known ntfy topic per location (from the Location Catalog). Users just
  subscribe to whichever predefined city topic they want, in the ntfy app
  directly (ideally via a deep link/QR code) — no registration flow, no
  per-user state on the platform side at all.
- This means there's nothing dynamic to register or track in phase 1 — the whole
  Subscriber Registry concept collapses into static reference data owned by the
  Location Catalog. Opting out is just unsubscribing in the ntfy app — invisible
  to the platform, but harmless, since there's no server-side state to clean up.
- User-provided locations and true per-user subscriptions are an explicit
  phase-2 idea; the Location Catalog's model (see §3.3) was deliberately kept
  generic — `locationId` as a plain key everywhere — so this can be added later
  as a new write path into an already-existing service, not a new bounded
  context.

## 4. End-to-end data flow (current design)

```
Location Catalog ──LocationActivated/Deactivated──> drives which locations
        │                                            Ingestion polls
        │
        ▼
Ingestion Service
   Trigger host (ACA Job/cron, or custom local scheduler) calls
   PollDispatchService.RunOnce()
        → checks active (Location, Provider) pairs vs. cadence + last-poll time
        → enqueues PollLocationRequested per due pair (per-provider queues)
        ↓
   Worker(s) (competing consumers, scale via KEDA/queue depth on Azure)
        → calls correct provider adapter (weather API or sensor)
        → normalizes to ForecastSnapshot or ObservationReading
        → persists to TimescaleDB (full history) — same DB transaction as outbox write
        → publishes ForecastSnapshotIngested / ObservationRecorded via
          MassTransit transactional outbox
        │
        ▼
Anomaly Detection Service
   - maintains rolling Baseline per (LocationId, ProviderId, TargetTime)
   - runs HazardRules per hazard, consuming Forecast Drift / Reality Divergence /
     Observation Trend deviations
   - applies hysteresis (persist across N evaluations) + idempotency
   - publishes AnomalyDetected
        │
        ▼
Notification Service
   - resolves LocationId → ntfy AlertChannel via Location Catalog
   - renders + delivers Alert to ntfy
   - logs DeliveryAttempt
```

## 5. Hosting & infrastructure strategy

**Target: Azure, but designed to be swappable / cloud-agnostic**, aiming to
stay within free tiers as long as possible (couple-dozen-user scale), adding a
paid tier only if/when user count grows enough to justify it.

### 5.1 Compute — Azure Container Apps (ACA), Consumption plan
- Free grant (standing monthly allowance, not a 12-month trial): 180,000
  vCPU-seconds, 360,000 GiB-seconds, 2 million requests/month, per subscription.
- **Only covers active usage — idle usage (minReplicas ≥ 1) is billed separately
  and is NOT covered by the free grant.** This is the key constraint driving
  design choices below.
- Poll Workers: designed to scale to zero via **KEDA** (RabbitMQ/Service Bus
  queue-length scaler) — fits the free grant well since there's no idle replica.
- Scheduler: explicitly redesigned around this constraint — an always-on
  Quartz.NET process would incur idle billing, so **ACA Jobs with a cron
  trigger** was chosen instead (jobs bill only for actual run duration, not idle
  time between runs). See §3.1.3 for full reasoning, including the
  rejected "self-perpetuating scheduled message chain" alternative.

### 5.2 Messaging — Azure Service Bus (Azure) / RabbitMQ (local), via MassTransit
- MassTransit abstracts the transport; Domain/Application code never references
  RabbitMQ.Client or Azure.Messaging.ServiceBus directly — only MassTransit's own
  abstractions (`IPublishEndpoint`, `IConsumer<T>`, message contracts).
- Service Bus **Basic tier** (no monthly base charge, ~$0.05/million operations)
  is sufficient for phase 1 because the current design has **no fan-out
  requirement** — every event type has exactly one consuming service. Basic tier
  only supports Queues, not Topics/Subscriptions (pub-sub fan-out) — Standard
  tier (~$10/month base charge) would only be needed if a future event needs
  independent multiple consumers.
- Per-provider queues confirmed as the design (avoids one slow/rate-limited
  provider stalling polling for others).
- Message **scheduling** (delayed delivery) is native to Service Bus (even Basic
  tier) but not to RabbitMQ without a plugin — MassTransit's own
  `IMessageScheduler` abstraction should be used instead of a transport-specific
  scheduling API, to preserve portability, if delayed-message scheduling is ever
  used in the messaging layer itself (distinct from the ACA Job cron scheduler
  discussed in §3.1.3).
- Duplicate detection is available natively on Service Bus Standard/Premium, but
  the platform's own idempotency keys (§3.1.6) remain the source of truth
  regardless — dedup windows are time-bounded, not a substitute for a real
  correctness guarantee.

### 5.3 Database — PostgreSQL + TimescaleDB, agnostic hosting
- **Azure Database for PostgreSQL Flexible Server has no always-free tier**, and
  even paid, only supports the **Apache-2 licensed edition** of TimescaleDB, not
  the full community/TSL-licensed edition (some compression/continuous-aggregate
  features may be unavailable there — worth checking the current licensing
  boundary against actual feature needs before committing to Azure-managed
  Postgres).
- **Decided: self-host Postgres+TimescaleDB (full community edition)**, hosting
  location kept agnostic — chosen host is the **Oracle Cloud Free Tier ARM VM**
  (currently 2 OCPU/12 GB always-free, reduced from a previous 4 OCPU/24 GB as of
  mid-2026 — worth re-checking current Oracle free tier terms periodically, as
  they've changed recently and inconsistently across account types).
- Portability achieved because TimescaleDB is just a Postgres extension — same
  SQL/EF Core/Npgsql code regardless of host. Only the **connection string** is
  environment-specific (externalized via configuration, never hardcoded).
- Local dev: Aspire spins up a `timescale/timescaledb` Docker image as part of
  the AppHost (drop-in Postgres image with the extension available).
- Hypertable setup (`create_hypertable(...)`) should live inside EF Core
  migrations, not as a manual runbook step, so it travels with the deployable
  artifact.
- Because Azure services reach across the open internet to the Oracle VM,
  connection must be hardened: TLS enforced, firewall allow-listing, strong
  rotated credentials, EF Core `EnableRetryOnFailure` for resiliency. Explicitly
  flagged as non-optional once the network path isn't local.
- Alternative considered: host Postgres on Azure's own compute instead
  (simpler single-cloud networking) — not free, low-cost, a legitimate trade-off
  if hybrid-cloud complexity isn't worth it. Not the chosen path, but worth
  knowing it was considered.

### 5.4 CI/CD
- GitHub Actions (2,000 free minutes/month private repos, unlimited public) +
  GitHub Container Registry for image hosting — covers the pipeline at no cost.

### 5.5 Secrets & configuration — industry-standard layered approach
- **Rule:** application code only ever asks `IConfiguration` for a value by key —
  never knows or branches on which underlying source provided it. This is what
  makes environment swaps invisible to Domain/Application/Infrastructure code,
  same discipline applied to messaging/database.
- **Local dev:** .NET User Secrets, plus Aspire parameters
  (`builder.AddParameter("x", secret: true)`) for automatic injection into
  service configuration without manual per-service wiring.
- **CI (GitHub Actions):** GitHub encrypted secrets, injected as environment
  variables into workflows.
- **Production (Azure):** **Azure Key Vault**, registered as an `IConfiguration`
  source via `Azure.Extensions.AspNetCore.Configuration.Secrets`
  (`AddAzureKeyVault(...)`), accessed via managed identity (no secret needed to
  fetch the secret). This is the general industry pattern across clouds
  ("secrets manager + workload identity"), not Azure-specific in principle.
- **Cloud-agnostic secrets goal:** user explicitly wants this abstracted too —
  if deploying outside Azure later, swap the Key Vault configuration provider
  registration for an equivalent one (HashiCorp Vault, AWS Secrets Manager, or
  even plain env vars for a bare VPS) — only the composition-root registration
  line changes, call sites (`configuration["..."]`) never do. Explicitly noted as
  lower priority to get "fully" agnostic on compared to messaging/database, since
  secrets management is usually the last thing anyone actually migrates in
  practice.

## 6. Cross-cutting concerns (approved, mostly implementation-time detail)

- **Outbox pattern:** MassTransit's EF Core transactional outbox (§3.1.4) —
  decided for Ingestion; same pattern applies wherever a service needs to persist
  + publish atomically (Detection publishing `AnomalyDetected` alongside
  persisting its `AnomalyEvent`, for instance).
- **Idempotency:** unique constraints / idempotency keys at each
  persist-then-publish boundary, given at-least-once delivery semantics
  throughout (§3.1.6, §3.2's AnomalyEvent idempotency key).
- **Database-per-service:** each service owns its own schema; no cross-service
  joins. Confirmed approach.
- **OpenTelemetry:** trace/correlation ID should flow from the ingestion trigger
  all the way to the ntfy push, so "why didn't I get notified at time X" is
  answerable end to end. Specific metrics of interest for Ingestion (flagged,
  not yet detailed): per-provider call latency and success rate, since "is this
  provider degrading" is a real operational question.
- **Testing split:** xUnit unit tests for pure domain logic (HazardRules,
  adapter normalization, PollDispatchService's due-check logic) with no
  infrastructure dependencies; Testcontainers-based integration tests for the
  Postgres/broker/outbox wiring, verifying the publish path actually works end
  to end. Concrete test scenarios explicitly deferred to implementation time.
- **Redis:** discussed as a possible future caching layer, explicitly **not**
  part of phase 1.

## 7. ntfy — how it actually works (important corrections made during discussion)

- ntfy is deliberately simple: **topic-based pub/sub over HTTP**. A topic is just
  a string. No accounts, no device registration, no location awareness built in
  — all of that is on the platform to build if wanted.
- Phase-1 model: **predefined public topics per predefined location**
  (e.g. Cluj-Napoca, Brașov, Oradea, Bucharest), owned/listed by the Location
  Catalog service. Users subscribe directly in the ntfy app, ideally via a deep
  link or QR code pointing at the known topic — no registration flow on the
  platform side.
- **Publish protection without self-hosting:** ntfy.sh (hosted) supports
  **access tokens** to protect who can publish/read a topic, and (on a paid tier)
  **topic reservations** which allow a write-only-for-others mode — exactly the
  broadcast shape needed (public subscribes, only the platform publishes).
  Without either, a topic name functions like a password; a long random suffix
  per city topic was floated as a low-cost stopgap given the low-stakes failure
  mode (someone spamming a wind-alert topic).
- **Self-hosting ntfy:** possible (same binary/Docker image), and gives full
  per-topic ACL control via `auth-default-access` + user accounts — but iOS
  requires the self-hosted instance to forward `poll_request` messages to an
  upstream server (e.g. ntfy.sh) for timely delivery, and Android instant
  delivery requires configuring your own Firebase Cloud Messaging key or falls
  back to a persistent connection (battery cost). **Decision: not worth
  self-hosting yet** — the ntfy.sh access-token route solves the actual problem
  (protecting publish) with zero extra infrastructure; self-hosting is a
  legitimate phase-2 item once scale or data-sensitivity actually demands it.

## 8. Explicit phase-1 vs. phase-2 boundary

**Phase 1 (current design target):**
- Predefined locations only, static Location Catalog, no user accounts.
- Open weather APIs only as providers.
- Storm/high-wind/flood hazards only.
- ntfy delivery to predefined public topics only.
- Self-hosted Postgres/TimescaleDB (Oracle free VM), Azure Container Apps +
  Service Bus Basic tier, GitHub Actions CI/CD.
- No Redis, no Subscriber Registry, no custom app/API, no paid tier.

**Phase 2 (explicitly named as future, not designed in detail yet):**
- User-submitted locations, real per-user Subscriber/LocationPreference concepts
  (the Location Catalog's model was kept generic specifically to allow this
  without restructuring).
- Additional specialized/hyperlocal weather APIs and home sensors beyond Google
  Home.
- Cross-provider consensus/corroboration detection (`ConsensusEvaluation`).
- Custom mobile app + public API (Notification Service growing a
  Gateway/BFF in front of it).
- Possibly self-hosted ntfy with Firebase-backed instant delivery, if scale or
  data control demands it.
- Possible paid tier to offset costs once user count grows.
- Redis caching, if/when it's actually needed.

## 9. Open items explicitly deferred to implementation time

These were raised and consciously left unresolved, to be decided when actually
writing code rather than in the abstract:
1. Exact shape of the provider adapter interface (`IWeatherProviderAdapter`?)
   and how new adapters get registered/wired (factory keyed by ProviderId?).
2. Specific Polly retry policies and transient-vs-permanent failure
   classification per provider.
3. Concrete OpenTelemetry instrumentation details beyond "trace correlation
   end-to-end" and "per-provider latency/success rate matters."
4. Concrete test scenarios/test project structure beyond "unit tests for pure
   domain logic, Testcontainers for infra integration."
5. Detection's rolling-observation-history storage shape: raw readings vs.
   pre-aggregated incremental stats (flexibility vs. query cost trade-off).
6. Whether Notification Service's Location→topic resolution should later move
   from a sync call to Location Catalog to a locally-cached denormalized read
   model (not needed at current scale, revisit if it becomes a real pain point).

## 10. Where the conversation left off

All major architectural decisions above are settled. The next step discussed was
starting to scaffold the **Ingestion Service** solution structure specifically —
project layout (Domain/Application/Infrastructure/Host split across
Scheduler-host, Worker-host, and a pluggable local-scheduler-host), the shared
provider adapter contract, and wiring up MassTransit + the EF Core outbox — with
the deferred items in §9 to be resolved as they come up naturally during that
work, not pre-decided.
