using Client.Web.Data;
using Client.Web.Models.Entities;
using Client.Web.Models.Identity;
using FoodOrdering.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Enums;
using Shouldly;

namespace FoodOrdering.IntegrationTests.Client;

[Collection(PostgresCollection.Name)]
public class ClientDbContextTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migrate_ShouldCreateEveryTableFromTheErd()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);

        List<string> tables = await context.Database
            .SqlQueryRaw<string>("SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public'")
            .ToListAsync();

        tables.ShouldBe(
            [
                "Categories", "MenuItems", "MenuItemExtras",
                "Carts", "CartItems", "CartItemExtras",
                "Orders", "OrderItems", "OrderItemExtras", "OrderStatusHistory", "Payments", "Ratings",
                "ProcessedMessages", "MessageLogs", "OutboxMessages",
                "Users", "Roles", "UserRoles", "UserClaims", "UserLogins", "UserTokens", "RoleClaims",
                "__EFMigrationsHistory"
            ],
            ignoreOrder: true);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldThrow_WhenTheOrderWasChangedByAnotherContext()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        Guid orderId = Guid.NewGuid();

        await using (ClientDbContext setup = postgres.NewDbContext<ClientDbContext>(connectionString))
        {
            ApplicationUser customer = NewCustomer();
            setup.Users.Add(customer);
            setup.Orders.Add(NewOrder(orderId, customer.Id));
            await setup.SaveChangesAsync();
        }

        await using ClientDbContext customerRequest = postgres.NewDbContext<ClientDbContext>(connectionString);
        await using ClientDbContext consumer = postgres.NewDbContext<ClientDbContext>(connectionString);

        Order cancelledByCustomer = await customerRequest.Orders.SingleAsync(o => o.Id == orderId);
        Order acceptedByAdmin = await consumer.Orders.SingleAsync(o => o.Id == orderId);

        cancelledByCustomer.Status = OrderStatus.Cancelled;
        await customerRequest.SaveChangesAsync();

        acceptedByAdmin.Status = OrderStatus.Accepted;
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => consumer.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldThrow_WhenTheMenuItemWasChangedByAnotherContext()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        Guid menuItemId = Guid.NewGuid();

        await using (ClientDbContext setup = postgres.NewDbContext<ClientDbContext>(connectionString))
        {
            var category = new Category { Id = Guid.NewGuid(), Name = "Burgers" };
            setup.Categories.Add(category);
            setup.MenuItems.Add(new MenuItem { Id = menuItemId, CategoryId = category.Id, Name = "Chicken Burger", Price = 8.50m });
            await setup.SaveChangesAsync();
        }

        await using ClientDbContext first = postgres.NewDbContext<ClientDbContext>(connectionString);
        await using ClientDbContext second = postgres.NewDbContext<ClientDbContext>(connectionString);

        MenuItem updatedByFirst = await first.MenuItems.SingleAsync(m => m.Id == menuItemId);
        MenuItem updatedBySecond = await second.MenuItems.SingleAsync(m => m.Id == menuItemId);

        updatedByFirst.Price = 9.00m;
        await first.SaveChangesAsync();

        updatedBySecond.Price = 9.50m;
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldThrow_WhenAPriceIsNegative()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        var category = new Category { Id = Guid.NewGuid(), Name = "Burgers" };
        context.Categories.Add(category);
        context.MenuItems.Add(new MenuItem { Id = Guid.NewGuid(), CategoryId = category.Id, Name = "Chicken Burger", Price = -1m });

        DbUpdateException exception = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());

        exception.InnerException!.Message.ShouldContain("CK_MenuItems_Price");
    }

    private static ApplicationUser NewCustomer() => new()
    {
        Id = Guid.NewGuid(),
        UserName = "sara@example.com",
        Email = "sara@example.com",
        FirstName = "Sara",
        LastName = "Haddad"
    };

    private static Order NewOrder(Guid id, Guid customerId) => new()
    {
        Id = id,
        OrderNumber = "FO-1042",
        CustomerId = customerId,
        DeliveryType = DeliveryType.Pickup,
        PaymentMethod = PaymentMethod.Cash,
        PaymentStatus = PaymentStatus.CashOnDelivery,
        Status = OrderStatus.Placed,
        Total = 12.50m,
        LastStatusChangedAt = DateTime.UtcNow
    };
}
