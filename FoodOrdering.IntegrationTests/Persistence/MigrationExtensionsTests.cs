using Admin.Web.Data;
using FoodOrdering.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Persistence;
using Shouldly;

namespace FoodOrdering.IntegrationTests.Persistence;

[Collection(PostgresCollection.Name)]
public class MigrationExtensionsTests(PostgresFixture postgres)
{
    [Fact]
    public async Task MigrateDatabaseAsync_ShouldApplyEveryMigration_WhenTheDatabaseIsEmpty()
    {
        string connectionString = postgres.NewDatabaseConnectionString();
        using IHost host = NewHost(connectionString);

        await host.MigrateDatabaseAsync<AdminDbContext>();

        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        (await context.Database.GetAppliedMigrationsAsync()).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task MigrateDatabaseAsync_ShouldDoNothing_WhenTheDatabaseIsUpToDate()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        using IHost host = NewHost(connectionString);

        await Should.NotThrowAsync(() => host.MigrateDatabaseAsync<AdminDbContext>());
    }

    private static IHost NewHost(string connectionString)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddDbContext<AdminDbContext>(options => options.UseNpgsql(connectionString));
        return builder.Build();
    }
}
