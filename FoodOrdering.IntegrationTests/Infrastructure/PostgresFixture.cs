using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace FoodOrdering.IntegrationTests.Infrastructure;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCommand("-c", "max_connections=500")
        .Build();

    public async Task<TContext> CreateDbContextAsync<TContext>() where TContext : DbContext
    {
        TContext context = NewDbContext<TContext>(NewDatabaseConnectionString());
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    public async Task<string> CreateMigratedDatabaseAsync<TContext>() where TContext : DbContext
    {
        string connectionString = NewDatabaseConnectionString();
        await using TContext context = NewDbContext<TContext>(connectionString);
        await context.Database.MigrateAsync();
        return connectionString;
    }

    public TContext NewDbContext<TContext>(string connectionString) where TContext : DbContext
    {
        DbContextOptions<TContext> options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(connectionString)
            .Options;

        return (TContext)Activator.CreateInstance(typeof(TContext), options)!;
    }

    public string NewDatabaseConnectionString() =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = $"test_{Guid.NewGuid():N}"
        }.ConnectionString;

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
