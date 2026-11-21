# FoodOrdering

Two ASP.NET Core MVC apps (`Client.Web`, `Admin.Web`) that talk through RabbitMQ, with shared code in `Shared.Contracts` and tests in `FoodOrdering.UnitTests` and `FoodOrdering.IntegrationTests`. See `README.md` to run locally and `docs/` for the decisions log and message format.

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
| `Contracts/` | `Shared.Contracts` (messages, enums, options, JSON) |
| `Client/Consumers/` | RabbitMQ consumers in Client.Web |
| `Client/Services/` | Services in Client.Web |
| `Admin/Consumers/` | RabbitMQ consumers in Admin.Web |
| `Admin/Services/` | Services in Admin.Web |

### Integration tests: `FoodOrdering.IntegrationTests`

Tests that need a real PostgreSQL, for example foreign keys, unique indexes, duplicate-message checks or anything saved by EF Core. Testcontainers starts a throwaway `postgres:16` container for the test run, so the `docker-compose.yml` containers and your dev data are never touched.

| Folder | Tests for |
|---|---|
| `Client/` | Database code in Client.Web |
| `Admin/` | Database code in Admin.Web |
| `Infrastructure/` | The test setup itself (`PostgresFixture`) |

- Mark the test class with `[Collection(PostgresCollection.Name)]` and take `PostgresFixture` in the constructor.
- Call `await postgres.CreateDbContextAsync<AdminDbContext>()` (or `ClientDbContext`) in each test. Every call creates a new empty database, so tests never share data.
- Don't use EF Core InMemory: it doesn't enforce foreign keys or unique indexes, so tests pass that would fail on the real database.
