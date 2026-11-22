# FoodOrdering

Two ASP.NET Core MVC apps (`Client.Web`, `Admin.Web`) that talk through RabbitMQ. Shared code is split in three: `Shared.Contracts` holds plain classes only (messages, enums, JSON options, routing keys and queue names), `Shared.Messaging` holds the RabbitMQ code (connection, topology, `AddRabbitMq`), and `Shared.Persistence` holds the EF Core code both apps use (`IAuditable`, `AuditInterceptor`). Tests are in `FoodOrdering.UnitTests` and `FoodOrdering.IntegrationTests`. See `README.md` to run locally and `docs/` for the decisions log and message format.

## Folder layout

`Client.Web` and `Admin.Web` have the same folders:

| Folder | What goes there |
|---|---|
| `Controllers/` | Receive the request, call a service, return a view. No database access and no business logic. |
| `Services/` | Business logic, as an interface and a class (`IOrderService`, `OrderService`). Calls repositories, never `DbContext`. |
| `Repositories/` | Database access only, as an interface and a class. The only place that uses the `DbContext`. |
| `Data/` | The `DbContext`, `Configurations/` (one per entity) and seeders |
| `Models/Entities/` | EF Core classes (database tables). Never sent to a view. |
| `Models/ViewModels/` | Classes sent to and from views, with data annotations for form validation |
| `Models/Identity/` | `ApplicationUser`, `ApplicationRole` |
| `Models/Enums/` | Enums used by only this app. Enums in messages live in `Shared.Contracts`. |
| `Messaging/Consumers/` | This app's RabbitMQ consumers |
| `Hubs/` | SignalR hubs |
| `Options/` | Settings classes used by only this app (e.g. `StripeOptions`) |

- Register every service and repository in `ServiceExtensions.cs` (`AddClientServices()` / `AddAdminServices()`), not in `Program.cs`. Use `AddScoped`, the same lifetime as the `DbContext`.
- `IExampleService` and `IExampleRepository` only show the pattern. Delete them in the PR that adds the first real service.

## Messaging

- Never add a RabbitMQ dependency to `Shared.Contracts`. RabbitMQ code goes in `Shared.Messaging`.
- Use the one `RabbitMqConnection` per app (`await connection.CreateChannelAsync()`). Never create a new `ConnectionFactory` or connection elsewhere.
- Never type exchange, queue or routing key names by hand: use `RoutingKeys`, `QueueNames` and `RabbitMqTopology`.

## Database

- Every business entity inherits `AuditableEntity` (`CreatedAt`, `UpdatedAt` with private setters). `ApplicationUser` can't, so it implements `IAuditable` directly. `AuditInterceptor` sets both on save. Each `DbContext` registers it in `OnConfiguring`.
- Money is a plain `decimal` (`numeric(10,2)`), not a value object. Every money column gets a `>= 0` check constraint named `CK_<Table>_<Column>` in its configuration.
- A time that comes from a message gets its own column (`PlacedAt`, `RatedAt`, `SourceUpdatedAt`), never `CreatedAt` or `UpdatedAt`.
- Keep `docs/erd-admin.*` and `docs/erd-client.*` in step with the entities in the same PR.
- In Development, each app applies pending migrations on startup with `await app.MigrateDatabaseAsync<TContext>()`, right after `builder.Build()`. Other environments never migrate on startup.

## Tests

- **A task with a Tests section is not done until its tests pass.** Write the tests together with the code, not at the end.
- Run all tests from the repo root with `dotnet test`. Docker Desktop must be running for the integration tests.
- Name tests `Method_Should<Result>_When<Situation>`, e.g. `Consume_ShouldIgnoreTheCancel_WhenTheOrderIsAlreadyAccepted`. Leave out `_When…` when there is no special situation, e.g. `Create_ShouldStartPlaced`.
- One test class per class under test, named `<ClassName>Tests`.
- Use NSubstitute for dependencies (`Substitute.For<IMessagePublisher>()`) and Shouldly for checks (`result.ShouldBe(...)`).

### Unit tests: `FoodOrdering.UnitTests`

Fast tests with no database, no RabbitMQ and no Docker. Put each test in the folder that matches the code it tests:

| Folder | Tests for |
|---|---|
| `Contracts/` | `Shared.Contracts` (messages, enums, JSON) |
| `Messaging/` | `Shared.Messaging` (options, topology) with a substituted `IChannel` |
| `Client/Consumers/` | RabbitMQ consumers in Client.Web |
| `Client/Services/` | Services in Client.Web |
| `Admin/Consumers/` | RabbitMQ consumers in Admin.Web |
| `Admin/Services/` | Services in Admin.Web |
| `Helpers/` | Shared test helpers, e.g. `MessageExamples` (reads the JSON examples from `docs/messages.md`) |

### Integration tests: `FoodOrdering.IntegrationTests`

Tests that need a real PostgreSQL or RabbitMQ, for example foreign keys, unique indexes, duplicate-message checks, anything saved by EF Core, or queues that must really exist. Testcontainers starts throwaway `postgres:16` and `rabbitmq:3-management` containers for the test run, so the `docker-compose.yml` containers and your dev data are never touched.

| Folder | Tests for |
|---|---|
| `Client/` | Database code in Client.Web |
| `Admin/` | Database code in Admin.Web |
| `Messaging/` | `Shared.Messaging` against a real RabbitMQ |
| `Persistence/` | `Shared.Persistence` against a real PostgreSQL |
| `Infrastructure/` | The test setup itself (`PostgresFixture`, `RabbitMqFixture`) |

- **Database tests:** mark the class with `[Collection(PostgresCollection.Name)]` and take `PostgresFixture` in the constructor. Call `await postgres.CreateDbContextAsync<AdminDbContext>()` (or `ClientDbContext`) in each test. Every call creates a new empty database, so tests never share data.
- **RabbitMQ tests:** mark the class with `[Collection(RabbitMqCollection.Name)]`, take `RabbitMqFixture` in the constructor, and use `rabbitMq.CreateHost()` to get a host wired with `AddRabbitMq` against the test container.
- Don't use EF Core InMemory: it doesn't enforce foreign keys or unique indexes, so tests pass that would fail on the real database.
