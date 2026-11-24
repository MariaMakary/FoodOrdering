using Client.Web;
using Client.Web.Data;
using Client.Web.Models.Identity;
using FoodOrdering.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace FoodOrdering.IntegrationTests.Client;

[Collection(PostgresCollection.Name)]
public class IdentitySeederTests(PostgresFixture postgres)
{
    [Fact]
    public async Task SeedRolesAsync_ShouldCreateTheCustomerRoleOnce_WhenItRunsTwice()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await using ServiceProvider services = new ServiceCollection()
            .AddLogging()
            .AddDbContext<ClientDbContext>(options => options.UseNpgsql(connectionString))
            .AddClientIdentity()
            .BuildServiceProvider();

        await IdentitySeeder.SeedRolesAsync(services);
        await IdentitySeeder.SeedRolesAsync(services);

        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        List<string?> roles = await context.Roles.Select(r => r.Name).ToListAsync();
        roles.ShouldBe([ClientRoles.Customer]);
    }
}
