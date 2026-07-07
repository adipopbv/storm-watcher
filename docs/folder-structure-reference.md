# Folder Structure Reference

This document presents the reference folder structure for a single **StormWatcher microservice / bounded context** following **Domain-Driven Design (DDD)** and **Clean Architecture**.

Every bounded context (Ingestion, Detection, Location Catalog, Notification, etc.) roughly follows this same structure, but the domain concepts change.

## Example

```text
src/
├── BoundedContext1/
│   ├── Domain/
│   │   ├── Common/
│   │   │   ├── Primitives/
│   │   │   ├── Exceptions/
│   │   │   ├── Specifications/
│   │   │   └── Interfaces/
│   │   ├── Aggregate1/
│   │   │   ├── Entities/
│   │   │   ├── ValueObjects/
│   │   │   ├── Events/
│   │   │   ├── Repositories/
│   │   │   ├── Services/
│   │   │   └── AggregateRoot1.cs
│   │   ├── Aggregate2/
│   │   │   ├── Entities/
│   │   │   ├── ValueObjects/
│   │   │   ├── Events/
│   │   │   ├── Repositories/
│   │   │   ├── Services/
│   │   │   └── AggregateRoot2.cs
│   │   └── ...
│   │
│   ├── Application/
│   │   ├── Common/
│   │   │   ├── Exceptions/
│   │   │   ├── Interfaces/
│   │   │   ├── Validation/
│   │   │   └── ...
│   │   ├── DTOs/
│   │   ├── Services/
│   │   ├── UseCase1/
│   │   ├── UseCase2/
│   │   ├── ...
│   │   └── DependencyInjection.cs
│   │
│   ├── Infrastructure/
│   │   ├── Persistence/
│   │   ├── Messaging/
│   │   ├── Authentication/
│   │   ├── Authorization/
│   │   ├── ExternalServices/
│   │   ├── BackgroundJobs/
│   │   ├── Telemetry/
│   │   ├── Configuration/
│   │   └── DependencyInjection.cs
│   │
│   ├── Api/
│   │   └── ...
│   │   
│   └── ...
│   
└── BoundedContext2/
    └── ...
```

## Notes

- Every bounded context follows the same overall structure, but adapting it to each particular context is expected.
- The Domain layer is organized around **aggregates and business concepts**, not technical concerns.
- Each aggregate has its own folder containing the aggregate root together with its entities, value objects, domain events, repository abstractions, and domain services. This keeps all concepts belonging to the aggregate together and improves discoverability.
- Aggregate roots live at the root of their feature folder (for example, `AggregateRoot1.cs`).
- Repository interfaces belong to the Domain layer; implementations belong to Infrastructure.
- The Application layer is organized around **business workflows and use cases**, rather than architectural patterns. If a bounded context naturally grows into a full CQRS or Vertical Slice Architecture, it can be refactored incrementally without changing the Domain model.
- Infrastructure contains all external concerns such as persistence, messaging, scheduling, provider integrations, authentication, and telemetry.
- The API project acts as the application's composition root in this example and remains intentionally thin. Depending on the microservice, it might not be needed, there might be multiple roots or be something else entirely.