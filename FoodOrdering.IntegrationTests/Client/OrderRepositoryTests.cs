using Client.Web.Data;
using Client.Web.Models.Identity;
using Client.Web.Repositories;
using FoodOrdering.IntegrationTests.Infrastructure;
using Shouldly;

namespace FoodOrdering.IntegrationTests.Client;

[Collection(PostgresCollection.Name)]
public class OrderRepositoryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task NextOrderNumberAsync_ShouldStartAt1000AndGoUpByOne()
    {
        await using ClientDbContext context = await postgres.CreateDbContextAsync<ClientDbContext>();
        var repository = new OrderRepository(context);

        long first = await repository.NextOrderNumberAsync();
        long second = await repository.NextOrderNumberAsync();
        long third = await repository.NextOrderNumberAsync();

        first.ShouldBe(1000);
        second.ShouldBe(1001);
        third.ShouldBe(1002);
    }

    [Fact]
    public async Task NextOrderNumberAsync_ShouldWorkOnAMigratedDatabase()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);

        long number = await new OrderRepository(context).NextOrderNumberAsync();

        number.ShouldBe(1000);
    }

    [Fact]
    public async Task GetCustomerAsync_ShouldReturnTheCustomer()
    {
        await using ClientDbContext context = await postgres.CreateDbContextAsync<ClientDbContext>();
        var seed = new ClientMenuSeed();
        await seed.SaveAsync(context);

        ApplicationUser? customer = await new OrderRepository(context).GetCustomerAsync(seed.Sara.Id);

        customer.ShouldNotBeNull();
        customer.DefaultAddress.ShouldBe("Hamra Street 12, Beirut");
    }

    [Fact]
    public async Task GetCustomerAsync_ShouldReturnNull_WhenTheCustomerDoesNotExist()
    {
        await using ClientDbContext context = await postgres.CreateDbContextAsync<ClientDbContext>();

        ApplicationUser? customer = await new OrderRepository(context).GetCustomerAsync(Guid.NewGuid());

        customer.ShouldBeNull();
    }
}
