# FoodOrdering

Two ASP.NET Core MVC apps that talk to each other through RabbitMQ:

| Project | Purpose |
|---|---|
| `Client.Web` | Customer app: browse, place and cancel orders |
| `Admin.Web` | Restaurant app: accept, prepare and deliver orders |
| `Shared.Contracts` | Message contracts and enums shared by both apps |

Each app has its own PostgreSQL database (`foodordering_client`, `foodordering_admin`).

## Run locally

### 1. Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (running)
- .NET SDK **10.0.404** or a newer 10.0 feature band. The version is pinned in `global.json`. Check with `dotnet --version`.

### 2. Create your `.env`

```powershell
Copy-Item .env.example .env   # Windows PowerShell
# cp .env.example .env        # macOS / Linux / Git Bash
```

Open `.env` and fill in a username and password for PostgreSQL and RabbitMQ. `.env` is git-ignored, so never commit it.

### 3. Start PostgreSQL and RabbitMQ

```bash
docker compose -f docker-compose.dev.yml up -d
docker compose -f docker-compose.dev.yml ps      # both should show "healthy"
```

On first start, `docker/init-db.sql` creates the `foodordering_client` and `foodordering_admin` databases.

| Service | Address | Login |
|---|---|---|
| PostgreSQL | `localhost:5432` | `POSTGRES_USER` / `POSTGRES_PASSWORD` from `.env` |
| RabbitMQ (AMQP) | `localhost:5672` | `RABBITMQ_DEFAULT_USER` / `RABBITMQ_DEFAULT_PASS` from `.env` |
| RabbitMQ Management UI | http://localhost:15672 | same as above |

Check that both databases exist:

```bash
docker exec -it foodordering-postgres psql -U <POSTGRES_USER> -c "\l"
```

### 4. Store connection settings in user-secrets

Both apps already have user-secrets set up (`UserSecretsId` is in each `.csproj`). Put **the same values you used in `.env`** in each app's secrets. Only secrets go here; everything else is in `appsettings.json` (see [Configuration](#configuration)).

```bash
# Client.Web
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=foodordering_client;Username=<POSTGRES_USER>;Password=<POSTGRES_PASSWORD>" --project Client.Web
dotnet user-secrets set "RabbitMq:Username" "<RABBITMQ_DEFAULT_USER>" --project Client.Web
dotnet user-secrets set "RabbitMq:Password" "<RABBITMQ_DEFAULT_PASS>" --project Client.Web

# Admin.Web
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=foodordering_admin;Username=<POSTGRES_USER>;Password=<POSTGRES_PASSWORD>" --project Admin.Web
dotnet user-secrets set "RabbitMq:Username" "<RABBITMQ_DEFAULT_USER>" --project Admin.Web
dotnet user-secrets set "RabbitMq:Password" "<RABBITMQ_DEFAULT_PASS>" --project Admin.Web
```

See what is stored with `dotnet user-secrets list --project Client.Web`.

If a secret is missing, the app stops at startup with a message like `RabbitMq:Password is missing. Set it in user-secrets.`

### 5. Run the apps

In two terminals:

```bash
dotnet run --project Client.Web
dotnet run --project Admin.Web
```

When an app starts, the console shows the RabbitMQ settings it is using (never the password):

```
RabbitMQ: localhost:5672, vhost /, exchange food.events, user foodordering
```

### Run the tests

```bash
dotnet test
```

Run it from the repo root. Tests are in `FoodOrdering.Tests` (xUnit, Moq, FluentAssertions, EF Core InMemory). The test rules are in `CLAUDE.md`.

### Stop / reset

```bash
docker compose -f docker-compose.dev.yml stop      # stop, keep data
docker compose -f docker-compose.dev.yml down      # remove containers, keep data
docker compose -f docker-compose.dev.yml down -v   # remove containers AND data (databases are recreated on next up)
```

### Troubleshooting

- **`Set POSTGRES_USER in .env`**: you have no `.env` file, or a value in it is empty. See step 2.
- **Port 5432 / 5672 / 15672 already in use**: another PostgreSQL or RabbitMQ is running on your machine. Stop it, or change the left side of the port mapping in `docker-compose.dev.yml`.
- **Databases missing after changing `init-db.sql`**: the script only runs when the data volume is empty. Run `down -v`, then `up -d`.

## Configuration

No password, key or connection string is ever written in code or in a committed file. Each setting comes from one of three places:

| Where | Used for | Committed? |
|---|---|---|
| `appsettings.json` | Non-secret defaults (host, port, exchange name, ...) | Yes |
| User-secrets | Secrets for local development | No (stored in `%APPDATA%\Microsoft\UserSecrets\`) |
| Environment variables | Secrets when the apps run in Docker (UNI-37) | No |

If a setting is in more than one place, the later one wins: `appsettings.json` → user-secrets → environment variables. In environment variables, `:` is written as `__` (two underscores), e.g. `RabbitMq__Password`.

### All settings (both apps)

| Setting | Default | Where it comes from | Secret? |
|---|---|---|---|
| `ConnectionStrings:DefaultConnection` | none | User-secrets / `ConnectionStrings__DefaultConnection` | Yes |
| `RabbitMq:Host` | `localhost` | `appsettings.json` | No |
| `RabbitMq:Port` | `5672` | `appsettings.json` | No |
| `RabbitMq:VirtualHost` | `/` | `appsettings.json` | No |
| `RabbitMq:ExchangeName` | `food.events` | `appsettings.json` | No |
| `RabbitMq:RetryCount` | `3` | `appsettings.json` | No |
| `RabbitMq:Username` | none | User-secrets / `RabbitMq__Username` | Yes |
| `RabbitMq:Password` | none | User-secrets / `RabbitMq__Password` | Yes |

The `RabbitMq` section is read into `Shared.Contracts.Options.RabbitMqOptions`. Use it in a class by injecting `IOptions<RabbitMqOptions>`.

Stripe (UNI-75) and AI (UNI-34) settings will be added here with their own options classes when those tickets are done.

### Changing a setting locally

Override any value from `appsettings.json` in user-secrets without editing the file:

```bash
dotnet user-secrets set "RabbitMq:Port" "5673" --project Client.Web
dotnet user-secrets remove "RabbitMq:Port" --project Client.Web
```

The second command removes the override, so the app goes back to the default from `appsettings.json`.
