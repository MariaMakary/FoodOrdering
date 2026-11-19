# FoodOrdering

Two ASP.NET Core MVC apps (`Client.Web`, `Admin.Web`) that talk through RabbitMQ, with shared code in `Shared.Contracts` and tests in `FoodOrdering.Tests`. See `README.md` to run locally and `docs/` for the decisions log and message format.

## Tests

- **A task with a Tests section is not done until its tests pass.** Write the tests together with the code, not at the end.
- Run all tests from the repo root with `dotnet test`.
- Name tests `Method_Situation_ExpectedResult`, e.g. `Consume_OrderAlreadyAccepted_IgnoresCancel`.
- One test class per class under test, named `<ClassName>Tests`.
- Put each test in the folder that matches the code it tests:

| Folder | Tests for |
|---|---|
| `Contracts/` | `Shared.Contracts` (messages, enums, options, JSON) |
| `Client/Consumers/` | RabbitMQ consumers in Client.Web |
| `Client/Services/` | Services in Client.Web |
| `Admin/Consumers/` | RabbitMQ consumers in Admin.Web |
| `Admin/Services/` | Services in Admin.Web |
| `Helpers/` | Shared test helpers |

- Use `TestDbFactory.Create<ClientDbContext>()` or `TestDbFactory.Create<AdminDbContext>()` for a fresh in-memory database in each test. Never share a database between tests.
- Use Moq for dependencies (e.g. the RabbitMQ publisher) and FluentAssertions (`.Should()`) for checks.
