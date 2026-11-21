using Admin.Web.Data;
using Admin.Web.Models.Entities;
using FoodOrdering.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Enums;
using Shouldly;

namespace FoodOrdering.IntegrationTests.Admin;

[Collection(PostgresCollection.Name)]
public class AdminDbContextTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migrate_ShouldCreateEveryTableFromTheErd()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);

        List<string> tables = await context.Database
            .SqlQueryRaw<string>("SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public'")
            .ToListAsync();

        tables.ShouldBe(
            [
                "Categories", "MenuItems", "MenuItemExtras",
                "Orders", "OrderItems", "OrderItemExtras", "OrderStatusHistory", "Payments", "Ratings",
                "ProcessedMessages", "MessageLogs",
                "Users", "Roles", "UserRoles", "UserClaims", "UserLogins", "UserTokens", "RoleClaims",
                "__EFMigrationsHistory"
            ],
            ignoreOrder: true);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldThrow_WhenTheOrderWasChangedByAnotherContext()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        Guid orderId = Guid.NewGuid();

        await using (AdminDbContext setup = postgres.NewDbContext<AdminDbContext>(connectionString))
        {
            setup.Orders.Add(NewOrder(orderId));
            await setup.SaveChangesAsync();
        }

        await using AdminDbContext staff = postgres.NewDbContext<AdminDbContext>(connectionString);
        await using AdminDbContext consumer = postgres.NewDbContext<AdminDbContext>(connectionString);

        Order acceptedByStaff = await staff.Orders.SingleAsync(o => o.Id == orderId);
        Order cancelledByConsumer = await consumer.Orders.SingleAsync(o => o.Id == orderId);

        acceptedByStaff.Status = OrderStatus.Accepted;
        await staff.SaveChangesAsync();

        cancelledByConsumer.Status = OrderStatus.Cancelled;
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => consumer.SaveChangesAsync());
    }

    private static Order NewOrder(Guid id) => new()
    {
        Id = id,
        OrderNumber = "FO-1042",
        CustomerId = Guid.NewGuid(),
        CustomerName = "Sara Haddad",
        CustomerPhone = "+961 70 123 456",
        DeliveryType = DeliveryType.Pickup,
        PaymentMethod = PaymentMethod.Cash,
        PaymentStatus = PaymentStatus.CashOnDelivery,
        Status = OrderStatus.Placed,
        Total = 12.50m,
        CreatedAt = DateTime.UtcNow,
        ReceivedAt = DateTime.UtcNow
    };
}
