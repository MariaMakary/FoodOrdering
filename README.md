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

Both apps already have user-secrets set up (`UserSecretsId` is in each `.csproj`). Put **the same values you used in `.env`** in each app's secrets:

```bash
# Client.Web
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=foodordering_client;Username=<POSTGRES_USER>;Password=<POSTGRES_PASSWORD>" --project Client.Web
dotnet user-secrets set "RabbitMQ:Host" "localhost" --project Client.Web
dotnet user-secrets set "RabbitMQ:Username" "<RABBITMQ_DEFAULT_USER>" --project Client.Web
dotnet user-secrets set "RabbitMQ:Password" "<RABBITMQ_DEFAULT_PASS>" --project Client.Web

# Admin.Web
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=foodordering_admin;Username=<POSTGRES_USER>;Password=<POSTGRES_PASSWORD>" --project Admin.Web
dotnet user-secrets set "RabbitMQ:Host" "localhost" --project Admin.Web
dotnet user-secrets set "RabbitMQ:Username" "<RABBITMQ_DEFAULT_USER>" --project Admin.Web
dotnet user-secrets set "RabbitMQ:Password" "<RABBITMQ_DEFAULT_PASS>" --project Admin.Web
```

See what is stored with `dotnet user-secrets list --project Client.Web`.

### 5. Run the apps

In two terminals:

```bash
dotnet run --project Client.Web
dotnet run --project Admin.Web
```

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
