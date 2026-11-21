# FoodOrdering

Two ASP.NET Core MVC apps that talk to each other through RabbitMQ:

| Project | Purpose |
|---|---|
| `Client.Web` | Customer app: browse, place and cancel orders |
| `Admin.Web` | Restaurant app: accept, prepare and deliver orders |
| `Shared.Contracts` | Message contracts and enums shared by both apps |

Each app has its own PostgreSQL container and database (`foodordering_client`, `foodordering_admin`).

## Run locally

### 1. Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (running)
- Any .NET 10 SDK (`global.json` requires 10.0.100 or newer). Check with `dotnet --version`.

### 2. Start PostgreSQL and RabbitMQ

```bash
docker compose up -d
docker compose ps      # all three should show "healthy"
```

| Service | Address | Login |
|---|---|---|
| PostgreSQL (Client.Web, `foodordering_client`) | `localhost:5432` | `postgres` / `postgres` |
| PostgreSQL (Admin.Web, `foodordering_admin`) | `localhost:5433` | `postgres` / `postgres` |
| RabbitMQ (AMQP) | `localhost:5672` | `guest` / `guest` |
| RabbitMQ Management UI | http://localhost:15672 | `guest` / `guest` |

These are dev-only credentials for containers on your own machine.

Check that both databases exist:

```bash
docker exec -it foodordering-client-postgres psql -U postgres -c "\l"
docker exec -it foodordering-admin-postgres psql -U postgres -c "\l"
```

### 3. Create `appsettings.Development.json` in each app

Both apps run in the `Development` environment, so each one loads `appsettings.Development.json` on top of `appsettings.json`. The file is git-ignored, so it's yours to change. Only secrets go here; everything else is in `appsettings.json` (see [Configuration](#configuration)).

`Client.Web/appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "ClientDb": "Host=localhost;Port=5432;Database=foodordering_client;Username=postgres;Password=postgres"
  },
  "RabbitMq": {
    "Username": "guest",
    "Password": "guest"
  }
}
```

`Admin.Web/appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "AdminDb": "Host=localhost;Port=5433;Database=foodordering_admin;Username=postgres;Password=postgres"
  },
  "RabbitMq": {
    "Username": "guest",
    "Password": "guest"
  }
}
```

If a setting is missing, the app stops at startup with a message like `RabbitMq:Password is missing. Set it in appsettings.Development.json.`

### 4. Run the apps

In two terminals:

```bash
dotnet run --project Client.Web
dotnet run --project Admin.Web
```

When an app starts, the console shows the RabbitMQ settings it is using (never the password):

```
RabbitMQ: localhost:5672, vhost /, exchange food.events, user guest
```

### Stop / reset

```bash
docker compose stop      # stop, keep data
docker compose down      # remove containers, keep data
docker compose down -v   # remove containers AND data (databases are recreated on next up)
```

### Troubleshooting

- **Port 5432 / 5433 / 5672 / 15672 already in use**: another PostgreSQL or RabbitMQ is running on your machine. Stop it, or change the left side of the port mapping in `docker-compose.yml`.
- **Connection string or RabbitMQ settings missing**: check that `appsettings.Development.json` exists in the app's folder and the app runs with `ASPNETCORE_ENVIRONMENT=Development` (the default in `launchSettings.json`).

## Configuration

No password, key or connection string is written in code. The only committed passwords are the dev-only container logins in `docker-compose.yml`. Each app setting comes from one of three places:

| Where | Used for | Committed? |
|---|---|---|
| `appsettings.json` | Non-secret defaults (host, port, exchange name, ...) | Yes |
| `appsettings.Development.json` | Secrets for local development | No (git-ignored) |
| Environment variables | Secrets when the apps run in Docker (UNI-37) | No |

If a setting is in more than one place, the later one wins: `appsettings.json` → `appsettings.Development.json` → environment variables. In environment variables, `:` is written as `__` (two underscores), e.g. `RabbitMq__Password`.

### All settings

| Setting | App | Default | Where it comes from | Secret? |
|---|---|---|---|---|
| `ConnectionStrings:ClientDb` | Client.Web | none | `appsettings.Development.json` / `ConnectionStrings__ClientDb` | Yes |
| `ConnectionStrings:AdminDb` | Admin.Web | none | `appsettings.Development.json` / `ConnectionStrings__AdminDb` | Yes |
| `RabbitMq:Host` | both | `localhost` | `appsettings.json` | No |
| `RabbitMq:Port` | both | `5672` | `appsettings.json` | No |
| `RabbitMq:VirtualHost` | both | `/` | `appsettings.json` | No |
| `RabbitMq:ExchangeName` | both | `food.events` | `appsettings.json` | No |
| `RabbitMq:RetryCount` | both | `3` | `appsettings.json` | No |
| `RabbitMq:Username` | both | none | `appsettings.Development.json` / `RabbitMq__Username` | Yes |
| `RabbitMq:Password` | both | none | `appsettings.Development.json` / `RabbitMq__Password` | Yes |

The `RabbitMq` section is read into `Shared.Contracts.Options.RabbitMqOptions`. Use it in a class by injecting `IOptions<RabbitMqOptions>`.

Stripe (UNI-75) and AI (UNI-34) settings will be added here with their own options classes when those tickets are done.

### Changing a setting locally

Override any value from `appsettings.json` by adding it to your `appsettings.Development.json`, for example:

```json
{
  "RabbitMq": {
    "Port": 5673
  }
}
```

Remove it again to go back to the default from `appsettings.json`.
