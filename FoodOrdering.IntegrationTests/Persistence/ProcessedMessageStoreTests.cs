using Admin.Web.Data;
using Admin.Web.Models.Entities;
using FoodOrdering.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared.Contracts.Enums;
using Shared.Contracts.Messages;
using Shared.Persistence.ProcessedMessages;
using Shouldly;

namespace FoodOrdering.IntegrationTests.Persistence;

[Collection(PostgresCollection.Name)]
public class ProcessedMessageStoreTests(PostgresFixture postgres)
{
    [Fact]
    public async Task IsProcessedAsync_ShouldBeFalse_WhenTheMessageIdWasNeverSaved()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);

        (await StoreFor(context).IsProcessedAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task MarkProcessed_ShouldSaveOneRow_WhenFollowedBySaveChanges()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        Guid messageId = Guid.NewGuid();

        await using (AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString))
        {
            StoreFor(context).MarkProcessed(messageId, nameof(OrderPlaced));
            await context.SaveChangesAsync();
        }

        await using AdminDbContext check = postgres.NewDbContext<AdminDbContext>(connectionString);
        ProcessedMessage row = await check.ProcessedMessages.SingleAsync();
        row.MessageId.ShouldBe(messageId);
        row.Type.ShouldBe(nameof(OrderPlaced));
        row.ProcessedAt.Kind.ShouldBe(DateTimeKind.Utc);
        (await StoreFor(check).IsProcessedAsync(messageId, CancellationToken.None)).ShouldBeTrue();
    }

    [Fact]
    public async Task MarkProcessed_ShouldSaveNothing_WhenSaveChangesIsNotCalled()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();

        await using (AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString))
        {
            StoreFor(context).MarkProcessed(Guid.NewGuid(), nameof(OrderPlaced));
        }

        await using AdminDbContext check = postgres.NewDbContext<AdminDbContext>(connectionString);
        (await check.ProcessedMessages.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task IsDuplicate_ShouldBeTrue_WhenTheSameMessageIdIsSavedTwice()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        Guid messageId = Guid.NewGuid();
        await using (AdminDbContext first = postgres.NewDbContext<AdminDbContext>(connectionString))
        {
            StoreFor(first).MarkProcessed(messageId, nameof(OrderPlaced));
            await first.SaveChangesAsync();
        }

        await using AdminDbContext second = postgres.NewDbContext<AdminDbContext>(connectionString);
        ProcessedMessageStore<AdminDbContext, ProcessedMessage> store = StoreFor(second);
        store.MarkProcessed(messageId, nameof(OrderPlaced));
        DbUpdateException exception = await Should.ThrowAsync<DbUpdateException>(() => second.SaveChangesAsync());

        store.IsDuplicate(exception).ShouldBeTrue();
    }

    [Fact]
    public async Task IsDuplicate_ShouldBeFalse_WhenAnotherUniqueIndexIsViolated()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        (Guid orderId, Guid menuItemId) = await SeedOrderAsync(connectionString);
        await using (AdminDbContext first = postgres.NewDbContext<AdminDbContext>(connectionString))
        {
            first.Ratings.Add(NewRating(orderId, menuItemId));
            await first.SaveChangesAsync();
        }

        await using AdminDbContext second = postgres.NewDbContext<AdminDbContext>(connectionString);
        second.Ratings.Add(NewRating(orderId, menuItemId));
        DbUpdateException exception = await Should.ThrowAsync<DbUpdateException>(() => second.SaveChangesAsync());

        exception.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        StoreFor(second).IsDuplicate(exception).ShouldBeFalse();
    }

    [Fact]
    public async Task IsDuplicate_ShouldBeFalse_WhenTheSaveFailsForAnotherReason()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        context.MenuItems.Add(new MenuItem { Id = Guid.NewGuid(), CategoryId = Guid.NewGuid(), Name = "Chicken Burger", Price = 9.50m });
        DbUpdateException exception = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());

        ProcessedMessageStore<AdminDbContext, ProcessedMessage> store = StoreFor(context);
        store.IsDuplicate(exception).ShouldBeFalse();
        store.IsDuplicate(new InvalidOperationException("Not a database error.")).ShouldBeFalse();
    }

    private static ProcessedMessageStore<AdminDbContext, ProcessedMessage> StoreFor(AdminDbContext context) => new(context);

    private static Rating NewRating(Guid orderId, Guid menuItemId) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        MenuItemId = menuItemId,
        CustomerName = "Sara Haddad",
        Stars = 5,
        RatedAt = DateTime.UtcNow
    };

    private async Task<(Guid OrderId, Guid MenuItemId)> SeedOrderAsync(string connectionString)
    {
        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        var category = new Category { Id = Guid.NewGuid(), Name = "Burgers" };
        var burger = new MenuItem { Id = Guid.NewGuid(), CategoryId = category.Id, Name = "Chicken Burger", Price = 9.50m };
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = "FO-1042",
            CustomerId = Guid.NewGuid(),
            CustomerName = "Sara Haddad",
            CustomerPhone = "+961 70 123 456",
            DeliveryType = DeliveryType.Pickup,
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.CashOnDelivery,
            Status = OrderStatus.Delivered,
            Total = 9.50m,
            PlacedAt = DateTime.UtcNow
        };
        context.Categories.Add(category);
        context.MenuItems.Add(burger);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return (order.Id, burger.Id);
    }
}
