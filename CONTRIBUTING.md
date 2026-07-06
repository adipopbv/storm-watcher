# Contributing to StormWatcher

Thanks for your interest in contributing! This project is early-stage but
follows standard collaborative practices from the start.

## Before you start

- Skim [`docs/weather-anomaly-platform-context.md`](docs/weather-anomaly-platform-context.md)
  — it captures the architecture decisions and reasoning already made. Align
  new work with it, and call out explicitly in your PR if you propose
  deviating from a decision documented there.
- Install the [Prerequisites](README.md#prerequisites) listed in the README.

## Project structure & layering

Each bounded context (`Ingestion`, `Detection`, `LocationCatalog`,
`Notification`) follows the same onion/clean-architecture layering:

| Layer | Responsibility | Rules |
|---|---|---|
| `*.Domain` | Entities, value objects, domain events, repository interfaces | No dependencies beyond the BCL |
| `*.Application` | Use case handlers, DTOs, orchestration, thin MassTransit consumers | Depends only on Domain |
| `*.Infrastructure` | EF Core, MassTransit transport config, external HTTP clients, outbox | Implements Application/Domain ports |
| `*.Host` | `Program.cs` composition root only | No business logic |

`StormWatcher.Contracts` holds integration event DTOs only — never domain or
business logic — so services stay decoupled through the message contracts
alone.

## Branching & commits

- Branch from `main`: `feature/<short-description>`, `fix/<short-description>`,
  `chore/<short-description>`.
- Prefer [Conventional Commits](https://www.conventionalcommits.org/) style
  messages (`feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`) — not
  strictly enforced, but keeps history readable.
- Keep commits focused; avoid mixing unrelated changes.

## Code style

- Formatting/naming is enforced via [`.editorconfig`](.editorconfig) — let your
  IDE apply it (Visual Studio and `dotnet format` both respect it).
- Nullable reference types and file-scoped namespaces are enabled
  project-wide; new code should follow suit.
- Favor small, composable registrations via each layer's
  `DependencyInjection.cs` extension method over logic in `Program.cs`.

## Testing

- Unit tests (xUnit) for pure domain/application logic — no infrastructure
  dependencies.
- Integration tests (xUnit + Testcontainers) for anything touching Postgres,
  MassTransit/RabbitMQ, or the outbox — require Docker running locally.
- Run the full suite before opening a PR:

  ```powershell
  dotnet test StormWatcher.slnx
  ```

## Pull requests

- Keep PRs scoped to one bounded context or concern where possible.
- Ensure `dotnet build` and `dotnet test` pass locally; CI (GitHub Actions)
  runs both on every PR.
- Describe *what* changed and *why*; link the relevant section of the
  architecture doc if it informed the change.

## Code of conduct

Participation in this project is governed by the
[Code of Conduct](CODE_OF_CONDUCT.md).
