# StormWatcher

> Weather anomaly alert platform — catches sudden, **unforecast** changes in storm,
> high-wind, and flood conditions and pushes real-time alerts. It does not aim to
> replace your everyday weather app; it exists purely to catch the moments where
> reality diverges from what the morning forecast promised.

<!-- Replace <owner>/<repo> once pushed to GitHub -->
[![CI](https://github.com/<owner>/<repo>/actions/workflows/ci.yml/badge.svg)](https://github.com/<owner>/<repo>/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

## Overview

Phase 1 scope is deliberately narrow:

- **Hazards:** storms, high winds, floods only (extensible later).
- **Data sources:** open weather APIs only (extensible later to hyperlocal APIs / home sensors).
- **Delivery:** [ntfy](https://ntfy.sh) push notifications only.
- **Users:** predefined public locations only, no user accounts yet.

## Architecture

C#/.NET 10, microservices with Domain-Driven Design, orchestrated locally via
.NET Aspire. Four bounded contexts, connected through MassTransit (RabbitMQ
locally, Azure Service Bus in the cloud) with an EF Core transactional outbox:

| Bounded context | Project prefix | Purpose |
|---|---|---|
| Weather Data Acquisition | `StormWatcher.Ingestion.*` | Polls providers on a schedule, normalizes forecasts/observations, persists authoritative history to TimescaleDB |
| Hazard Detection | `StormWatcher.Detection.*` | Compares live data against rolling baselines (forecast drift, reality divergence, observation trend) and raises anomalies |
| Location Reference | `StormWatcher.LocationCatalog.*` | Source of truth for which locations are active and how alerts reach them |
| Alert Delivery | `StormWatcher.Notification.*` | Renders and delivers alerts to ntfy, tracks delivery attempts |

```
Location Catalog ──LocationActivated/Deactivated──> drives which locations
													 Ingestion polls
Ingestion  → PollDispatchService dispatches due polls → Worker(s) normalize
		   → persist + publish ForecastSnapshotIngested / ObservationRecorded
Detection  → maintains rolling Baseline, evaluates HazardRules, publishes AnomalyDetected
Notification → resolves ntfy topic via Location Catalog, renders + delivers Alert
```

Tech stack: PostgreSQL + TimescaleDB, EF Core, MassTransit, xUnit + Testcontainers,
OpenTelemetry, GitHub Actions, Docker — designed to stay cloud-agnostic (Azure is
the primary target, but every infrastructure dependency is swappable via
configuration).

Full design rationale, domain model, and open questions:
[`docs/weather-anomaly-platform-context.md`](docs/weather-anomaly-platform-context.md).

## Repository layout

```
StormWatcher/
├─ aspire/                 # .NET Aspire AppHost — local orchestration (Postgres/TimescaleDB, RabbitMQ, all services)
├─ src/
│  ├─ Ingestion/           # Weather Data Acquisition bounded context
│  ├─ Detection/           # Hazard Detection bounded context
│  ├─ LocationCatalog/     # Location Reference bounded context
│  ├─ Notification/        # Alert Delivery bounded context
│  └─ Shared/               # ServiceDefaults (Aspire), Contracts (integration events)
├─ tests/                  # Unit + integration tests, mirrored per bounded context
├─ docs/                   # Architecture & design context
└─ *.slnx / *.slnf         # Root solution + per-context solution filters
```

## Prerequisites

- .NET 10 SDK (pinned via [`global.json`](global.json))
- Docker Desktop (or compatible engine) — required for Aspire's Postgres/TimescaleDB
  and RabbitMQ containers, and for Testcontainers-based integration tests
- Visual Studio 2026 with the .NET Aspire workload, or the `dotnet` CLI

## Getting started

```powershell
git clone https://github.com/<owner>/StormWatcher.git
cd StormWatcher
dotnet restore StormWatcher.slnx
```

Run everything locally via Aspire (spins up Postgres+TimescaleDB, RabbitMQ, and
all service hosts, with the Aspire dashboard for logs/traces):

```powershell
dotnet run --project aspire/StormWatcher.AppHost
```

Or open [`StormWatcher.slnx`](StormWatcher.slnx) in Visual Studio and launch the
**StormWatcher.AppHost** project.

### Working on a single bounded context

Open the matching solution filter to reduce noise to just that context plus its
shared dependencies:

- [`StormWatcher.Ingestion.slnf`](StormWatcher.Ingestion.slnf)
- [`StormWatcher.Detection.slnf`](StormWatcher.Detection.slnf)
- [`StormWatcher.LocationCatalog.slnf`](StormWatcher.LocationCatalog.slnf)
- [`StormWatcher.Notification.slnf`](StormWatcher.Notification.slnf)

## Running tests

```powershell
dotnet test StormWatcher.slnx
```

Integration test projects use Testcontainers and require Docker to be running.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) — includes layering rules, branch/commit
conventions, and the testing checklist expected on PRs.

## License

[MIT](LICENSE)

## Project status

Phase 1 — early scaffolding. Predefined locations, storm/wind/flood hazards, ntfy
delivery, no user accounts. See §8 of the architecture doc for the full
phase-1/phase-2 boundary.
