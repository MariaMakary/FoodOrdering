# FoodOrdering

Two ASP.NET Core MVC apps: `Client.Web` (customers order, pay, cancel and rate) and `Admin.Web` (the restaurant manages the menu and the kitchen). Each app has its own PostgreSQL database (`foodordering_client`, `foodordering_admin`). **The apps never call each other or read each other's database.** Data moves only through RabbitMQ messages.

Shared code is split in three:

| Project | Holds | Depends on |
|---|---|---|
| `Shared.Contracts` | Plain classes only: messages, enums, JSON options, routing keys and queue names | nothing |
| `Shared.Messaging` | RabbitMQ code: connection, topology, `AddRabbitMq` | `Shared.Contracts`, RabbitMQ.Client |
| `Shared.Persistence` | EF Core code both apps use: `AuditableEntity`, `IAuditable`, `AuditInterceptor`, `MigrateDatabaseAsync`, and the outbox (`Outbox/`) | EF Core, `Shared.Messaging` |

Tests are in `FoodOrdering.UnitTests` and `FoodOrdering.IntegrationTests`.

## How to run

Follow [README.md](README.md): `docker compose up -d`, create `appsettings.Development.json` in each app, `dotnet tool restore`, then `dotnet run`. Secrets go only in the git-ignored `appsettings.Development.json`. We don't use `dotnet user-secrets` or `.env` files.

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
| `Hubs/` | SignalR hubs and the notifiers that send their events (`IOrderNotifier`, `OrderHubNotifier`) |
| `Options/` | Settings classes used by only this app (e.g. `StripeOptions`) |

- Register every service and repository in `ServiceExtensions.cs` (`AddClientServices()` / `AddAdminServices()`), not in `Program.cs`. Use `AddScoped`, the same lifetime as the `DbContext`. A SignalR notifier is `AddSingleton`, because it only wraps the singleton `IHubContext`.
- `IExampleService` and `IExampleRepository` in `Admin.Web` only show the pattern. Delete them in the PR that adds Admin's first real service (Client's were deleted in UNI-63).

## Code rules

- **Async all the way:** every method that touches the database, RabbitMQ or HTTP is `async`, ends in `Async`, and takes a `CancellationToken`. Never call `.Result` or `.Wait()`.
- **Controllers only call services.** Services only call repositories. A controller never uses a `DbContext`, and a service never builds an EF query.
- **Views get ViewModels, never entities.** A form binds to an input ViewModel, never to an entity, so a user can't post fields they shouldn't set (`Status`, `Total`).
- **Depend on interfaces.** Classes ask for `IOrderService`, not `OrderService`, and get it through constructor injection.
- **Naming:** `PascalCase` for types, methods and properties; `_camelCase` for private fields; interfaces start with `I`. Use primary constructors for injected dependencies (`public class OrderService(IOrderRepository orders)`).
- **Settings** are read with the Options pattern (`IOptions<RabbitMqOptions>`). Never type a password, key, connection string or URL in code.
- **No comments in code or config files.** Use clear names. Explanations go in the PR description or `docs/`.
- Dates are always UTC: use `DateTime.UtcNow`, never `DateTime.Now`.

## Build and packages

- `Directory.Build.props` sets `net10.0`, `Nullable`, `ImplicitUsings` and `TreatWarningsAsErrors` for every project. Don't repeat them in a `.csproj`.
- **Every package version is in `Directory.Packages.props`.** A `.csproj` has only `<PackageReference Include="..." />` with no `Version`. To add or update a package, change its version there, so every project uses the same version.
- `.editorconfig` holds the formatting and naming rules. `dotnet build` must have 0 warnings.

## Database

- **Every primary key is a `Guid`.** An order keeps the same Id in both databases, and the Client's menu tables keep the Admin's Ids.
- Identity uses `ApplicationUser : IdentityUser<Guid>` and `ApplicationRole : IdentityRole<Guid>`, and the tables are renamed without the `AspNet` prefix (`Users`, `Roles`, `UserRoles`, ...).
- Every business entity inherits `AuditableEntity` (`CreatedAt`, `UpdatedAt` with private setters). `ApplicationUser` can't, so it implements `IAuditable` directly. `AuditInterceptor` sets both on save. Each `DbContext` registers it in `OnConfiguring`.
- Times are UTC `timestamptz`. A time that comes from a message gets its own column (`PlacedAt`, `RatedAt`, `SourceUpdatedAt`), never `CreatedAt` or `UpdatedAt`.
- Money is a plain `decimal` (`numeric(10,2)`), not a value object. Every money column gets a `>= 0` check constraint named `CK_<Table>_<Column>` in its configuration.
- Enums are stored as text (`HaveConversion<string>()` in `ConfigureConventions`), the same strings as in the messages.
- Database rules go in a configuration class in `Data/Configurations/` (Fluent API), not as data annotations on the entity.
- A row that two writers can change at the same time (`Order`, and `MenuItem` in Client) has the `xmin` concurrency token. Catch `DbUpdateConcurrencyException` and retry or show a message; never ignore it.
- Create migrations with `dotnet ef migrations add <Name> --project <App>`. **Read every generated migration before committing it**, and put any data backfill SQL in the migration itself.
- In Development, each app applies pending migrations on startup with `await app.MigrateDatabaseAsync<TContext>()`, right after `builder.Build()`. Other environments never migrate on startup.
- Keep `docs/erd-admin.*` and `docs/erd-client.*` in step with the entities in the same PR.

## Messaging

The 6 messages, their fields and JSON examples are in [docs/messages.md](docs/messages.md). The exchange and queue design is DEC-002 in [docs/decisions-log.md](docs/decisions-log.md).

- Never add a RabbitMQ dependency to `Shared.Contracts`. RabbitMQ code goes in `Shared.Messaging`.
- Use the one `RabbitMqConnection` per app (`await connection.CreateChannelAsync()`). Never create a new `ConnectionFactory` or connection elsewhere.
- Never type exchange, queue or routing key names by hand: use `RoutingKeys`, `QueueNames` and `RabbitMqTopology`.
- **Publish only through the outbox** (DEC-004): inject `IOutbox`, call `outbox.Add(message)`, then the normal `SaveChangesAsync`, so the data change and the message are saved together or not at all. Never call `IMessagePublisher` from a service, controller or consumer; only `OutboxDispatcher` does. Each app registers it with `builder.Services.AddOutbox<TContext>()` and maps the table with `modelBuilder.AddOutbox()`.
- A message type can only go in the outbox if it has a queue in `RabbitMqTopology.Bindings`.
- **Consumers use manual acks** (UNI-16): ack only after the database save succeeded. A failing message is retried 5 times about 200 ms apart, then nacked with `requeue: false` so it goes to its `.error` queue (UNI-17). Never catch an exception just to log it and ack.
- **Every consumer checks `ProcessedMessage` first** (UNI-18) and saves the `MessageId` in the same transaction as its change, so a duplicate message does nothing.
- A message is a full snapshot. The receiver never needs to ask the sender for more data.

## Order statuses

The full rules, the cancel race and the scenarios are DEC-001 in [docs/decisions-log.md](docs/decisions-log.md). Any change not in this table is refused and logged as a warning.

| From | To | Who | Message |
|---|---|---|---|
| (new) | Placed | Customer (Client) | `OrderPlaced` |
| Placed | Cancelled | Customer (Client) | `OrderCancelled` |
| Placed | Accepted (+ETA) or Rejected (+reason) | Staff (Admin) | `OrderStatusChanged` |
| Accepted → Preparing → Ready | next step | Staff (Admin) | `OrderStatusChanged` |
| Ready | OutForDelivery | Staff (Admin), Delivery orders only | `OrderStatusChanged` |
| Ready / OutForDelivery | Delivered | Staff (Admin) | `OrderStatusChanged` |

- **Admin owns the order status.** The Client sets only `Placed` and `Cancelled`, and always applies Admin's `OrderStatusChanged`, even over its own `Cancelled`.
- `Delivered`, `Rejected` and `Cancelled` are final.
- The Client ignores an `OrderStatusChanged` that is not newer than `Order.LastStatusChangedAt`, unless the Client shows `Cancelled`.

## Real-time (SignalR)

Hubs live in `Hubs/`. Services send events after the save succeeded, through a small notifier interface in `Hubs/` (`IOrderNotifier`) whose class wraps `IHubContext<THub>`, so services don't depend on SignalR types. Use exactly these names, because the JavaScript listens for them.

| App | Hub | URL | Who can connect | Event | Parameters | Sent to |
|---|---|---|---|---|---|---|
| Client | `OrderHub` | `/hubs/orders` | Customer | `OrderStatusUpdated` | `orderId`, `newStatus`, `estimatedTime` | `Clients.User(customerId)`, only the order's owner |
| Admin | `AdminHub` | `/hubs/admin` | Any staff | `NewOrder` | `orderCard` (`OrderCardViewModel`) | `Clients.All` |
| Admin | `AdminHub` | `/hubs/admin` | Any staff | `OrderUpdated` | `orderId`, `status`, `isPaid` | `Clients.All` |

**Client `OrderHub` (UNI-71)**

- `[Authorize(Roles = ClientRoles.Customer)]`, mapped in `Program.cs` with `app.MapHub<OrderHub>(OrderHub.Path)`. SignalR is registered by `AddClientSignalR()`, which `AddClientServices()` calls. The hub has no methods the browser can call; it only receives events.
- A request without a login gets `401` (not a redirect to the login page), and a logged-in user without the `Customer` role gets `403`.
- The SignalR user id is the default `ClaimTypes.NameIdentifier` claim, which Identity fills with the user's Guid, so `Clients.User(order.CustomerId.ToString())` reaches every open tab of that customer and nobody else.
- `OrderStatusService.ApplyStatusChangeAsync` calls `IOrderNotifier.OrderStatusUpdatedAsync` once, only after `SaveChangesAsync` saved a change. An unknown order, a stale message or a repeated message pushes nothing.
- If the push fails, the error is logged as a warning and the message is still handled. The database is the source of truth and the page reloads its data, so a lost push is never retried.
- The event has 3 positional arguments. Enums are sent as text, like in the messages: `connection.on("OrderStatusUpdated", (orderId, newStatus, estimatedTime) => ...)`.

| Argument | JSON type | Example | Notes |
|---|---|---|---|
| `orderId` | string (Guid) | `"7c9e6679-7425-40de-944b-e07fc1f90ae7"` | |
| `newStatus` | string (`OrderStatus` name) | `"Accepted"` | Never a number |
| `estimatedTime` | string (UTC ISO 8601) or `null` | `"2026-11-24T13:15:00Z"` | The order's saved ETA, kept from an earlier change when the message has none |

## Authorization

Both apps use ASP.NET Core Identity with cookies (no JWT).

- Each app registers Identity in `ServiceExtensions.cs` (`AddClientIdentity()`), not in `Program.cs`, so the tests use the same settings.
- **Each app has its own auth cookie name** (`FoodOrdering.Client.Auth`, `FoodOrdering.Admin.Auth`). Browsers share cookies across ports on `localhost`, so with the default name, logging in to one app would log you out of the other.
- Roles are seeded on startup in every environment by `Data/IdentitySeeder.cs`, only if they don't exist.
- Write role names with the constants (`ClientRoles.Customer`), never as a typed string.
- Controllers call `IAccountService`, never `UserManager` or `SignInManager` directly.

**Client** has one role, `Customer`. Every new user gets it when they register. Password rules: at least 8 characters with an uppercase letter, a lowercase letter and a digit. 5 wrong passwords lock the account for 5 minutes.

| Controller | Access |
|---|---|
| `Home`, `Menu`, `Account` (Register, Login) | Public |
| `Cart`, `Checkout`, `Orders`, `Ratings`, `Payment`, `Account` (Profile, ChangePassword, Logout), `OrderHub` (`/hubs/orders`) | `[Authorize(Roles = "Customer")]` |
| `Payment.Webhook` | `[AllowAnonymous]`, checked with the Stripe signature |

**Admin** has the roles `Admin`, `Staff` and `Manager`, and no public registration. A fallback policy requires login on every page unless it has `[AllowAnonymous]` (only `Account.Login`).

| Policy | Roles | Used by |
|---|---|---|
| (logged in) | any staff | `Dashboard`, `Orders.Index`, `Orders.Details`, `AdminHub` |
| `CanManageOrders` | Admin, Staff | `Orders.Board`, `Orders.Accept`, `Orders.Reject`, `Orders.UpdateStatus`, `MenuItems.Index`, `MenuItems.Details`, `MenuItems.ToggleAvailability` |
| `CanViewReports` | Admin, Manager | `Ratings`, `Reports` |
| `AdminOnly` | Admin | `Categories`, `MenuItems` Create/Edit/Delete, `Staff`, `Messages` |

Use `[Authorize(Policy = "...")]` with these names, never `[Authorize(Roles = ...)]` in Admin.

## UI

- Bootstrap 5 in both apps. No other CSS framework.
- Reuse the shared partials in `Views/Shared/` instead of copying markup: `_StatusBadge` (order status), `_DishCard` (menu item), `_Stars` (rating).

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
| `Persistence/` | `Shared.Persistence` code that needs no database (e.g. `OutboxMessage`) |
| `Client/` | Client.Web app setup, e.g. `ErrorHandlingExtensions` with an in-memory `TestServer` |
| `Client/Hubs/` | SignalR hubs and notifiers in Client.Web (hub access with an in-memory `TestServer`, notifier with a substituted `IHubContext`) |
| `Client/Consumers/` | RabbitMQ consumers in Client.Web |
| `Client/Services/` | Services in Client.Web |
| `Admin/` | Admin.Web app setup, e.g. `ErrorHandlingExtensions` with an in-memory `TestServer` |
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

## Git

- One Linear issue per branch. Branch from the latest `develop` and use the branch name Linear gives (`makarymaria79/uni-6-...`).
- Commit messages start with the issue Id: `UNI-6 Set up EF Core and PostgreSQL in Client.Web`.
- Open the PR into `develop`. It needs 1 approval from the other teammate. Nobody pushes to `develop` or `main` directly.
- For a branch built on another branch that isn't merged yet, rebase it onto `develop` after the first one merges: `git rebase --onto develop <old-base> <branch>`.
- When a decision changes, update this file and `docs/decisions-log.md` in the same PR.

## Links

- [docs/decisions-log.md](docs/decisions-log.md): DEC-001 order statuses, DEC-002 exchange and queues, DEC-003 audit columns, DEC-004 outbox
- [docs/messages.md](docs/messages.md): the 6 messages with JSON examples
- [docs/erd-client.md](docs/erd-client.md) and [docs/erd-admin.md](docs/erd-admin.md): the database diagrams and rules
- Linear: [Decisions Log](https://linear.app/onlinefoodordering/document/decisions-log-85ab510ce1df) (early decisions and demo questions) and [Endpoints & Pages Catalogue](https://linear.app/onlinefoodordering/document/endpoints-and-pages-catalogue-6f73ed46121b) (every route, its access and its issue)
