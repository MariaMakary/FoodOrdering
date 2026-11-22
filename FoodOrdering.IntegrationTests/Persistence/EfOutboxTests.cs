using Client.Web.Data;
using Client.Web.Models.Entities;
using FoodOrdering.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Messages;
using Shared.Persistence.Outbox;
using Shouldly;

namespace FoodOrdering.IntegrationTests.Persistence;

[Collection(PostgresCollection.Name)]
public class EfOutboxTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Add_ShouldSaveOneRow_WhenFollowedBySaveChanges()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        OrderCancelled message = NewMessage();

        await using (ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString))
        {
            new EfOutbox<ClientDbContext>(context).Add(message);
            await context.SaveChangesAsync();
        }

        await using ClientDbContext check = postgres.NewDbContext<ClientDbContext>(connectionString);
        OutboxMessage row = await check.Set<OutboxMessage>().SingleAsync();
        row.Id.ShouldBe(message.MessageId);
        row.Type.ShouldBe(nameof(OrderCancelled));
        row.SentAt.ShouldBeNull();
        row.ToMessage().ShouldBe(message);
    }

    [Fact]
    public async Task Add_ShouldSaveNothing_WhenSaveChangesIsNotCalled()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();

        await using (ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString))
        {
            new EfOutbox<ClientDbContext>(context).Add(NewMessage());
        }

        await using ClientDbContext check = postgres.NewDbContext<ClientDbContext>(connectionString);
        (await check.Set<OutboxMessage>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Add_ShouldSaveNothing_WhenTheDataChangeInTheSameSaveFails()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();

        await using (ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString))
        {
            var category = new Category { Id = Guid.NewGuid(), Name = "Burgers" };
            context.Categories.Add(category);
            context.MenuItems.Add(new MenuItem { Id = Guid.NewGuid(), CategoryId = category.Id, Name = "Chicken Burger", Price = -1m });
            new EfOutbox<ClientDbContext>(context).Add(NewMessage());

            await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        await using ClientDbContext check = postgres.NewDbContext<ClientDbContext>(connectionString);
        (await check.Set<OutboxMessage>().CountAsync()).ShouldBe(0);
    }

    private static OrderCancelled NewMessage()
    {
        Guid orderId = Guid.NewGuid();
        return new OrderCancelled
        {
            OrderId = orderId,
            CorrelationId = orderId,
            Reason = "Changed my mind",
            CancelledAt = DateTime.UtcNow
        };
    }
}
