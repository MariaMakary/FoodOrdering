using Client.Web.Data;
using Client.Web.Models.Entities;
using Client.Web.Repositories;
using FoodOrdering.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared.Contracts.Enums;
using Shouldly;

namespace FoodOrdering.IntegrationTests.Client;

[Collection(PostgresCollection.Name)]
public class RatingRepositoryTests(PostgresFixture postgres)
{
    private readonly ClientMenuSeed _seed = new();

    [Fact]
    public async Task SaveChangesAsync_ShouldFailOnTheUniqueIndex_WhenTheSameDishIsRatedTwiceForOneOrder()
    {
        (string connectionString, Guid orderId) = await SeedDeliveredOrderAsync();

        await using (ClientDbContext first = postgres.NewDbContext<ClientDbContext>(connectionString))
        {
            first.Ratings.Add(NewRating(orderId, 5));
            await first.SaveChangesAsync();
        }

        await using ClientDbContext second = postgres.NewDbContext<ClientDbContext>(connectionString);
        second.Ratings.Add(NewRating(orderId, 1));

        DbUpdateException exception = await Should.ThrowAsync<DbUpdateException>(() => second.SaveChangesAsync());
        PostgresException postgresError = exception.InnerException.ShouldBeOfType<PostgresException>();
        postgresError.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        postgresError.ConstraintName.ShouldBe("IX_Ratings_OrderId_MenuItemId");
    }

    [Fact]
    public async Task TrySaveChangesAsync_ShouldReturnFalseAndDiscardTheChanges_WhenTheDishIsAlreadyRatedForTheOrder()
    {
        (string connectionString, Guid orderId) = await SeedDeliveredOrderAsync();

        await using (ClientDbContext first = postgres.NewDbContext<ClientDbContext>(connectionString))
        {
            var repository = new RatingRepository(first);
            repository.Add(NewRating(orderId, 5));
            (await repository.TrySaveChangesAsync()).ShouldBeTrue();
        }

        await using ClientDbContext second = postgres.NewDbContext<ClientDbContext>(connectionString);
        var secondRepository = new RatingRepository(second);
        secondRepository.Add(NewRating(orderId, 1));

        (await secondRepository.TrySaveChangesAsync()).ShouldBeFalse();
        second.ChangeTracker.Entries().ShouldBeEmpty();
        (await second.Ratings.SingleAsync()).Stars.ShouldBe(5);
    }

    [Fact]
    public async Task TrySaveChangesAsync_ShouldStillThrow_WhenTheSaveFailsForAnotherReason()
    {
        (string connectionString, Guid orderId) = await SeedDeliveredOrderAsync();

        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        var repository = new RatingRepository(context);
        repository.Add(NewRating(orderId, 9));

        DbUpdateException exception = await Should.ThrowAsync<DbUpdateException>(() => repository.TrySaveChangesAsync());
        exception.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task GetOrderForRatingAsync_ShouldLoadTheItemsTheirDishesAndTheRatingsWithoutTracking()
    {
        (string connectionString, Guid orderId) = await SeedDeliveredOrderAsync();
        await using (ClientDbContext setup = postgres.NewDbContext<ClientDbContext>(connectionString))
        {
            setup.Ratings.Add(NewRating(orderId, 4));
            await setup.SaveChangesAsync();
        }

        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        Order? order = await new RatingRepository(context).GetOrderForRatingAsync(orderId);

        order.ShouldNotBeNull();
        order.Items.ShouldHaveSingleItem().MenuItem.Name.ShouldBe("Chicken Burger");
        order.Ratings.ShouldHaveSingleItem().Stars.ShouldBe(4);
        context.ChangeTracker.Entries().ShouldBeEmpty();
    }

    private Rating NewRating(Guid orderId, int stars) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        MenuItemId = _seed.ChickenBurger.Id,
        CustomerId = _seed.Sara.Id,
        Stars = stars
    };

    private async Task<(string ConnectionString, Guid OrderId)> SeedDeliveredOrderAsync()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        await _seed.SaveAsync(context);

        Guid orderId = Guid.NewGuid();
        context.Orders.Add(new Order
        {
            Id = orderId,
            OrderNumber = "FO-1000",
            CustomerId = _seed.Sara.Id,
            DeliveryType = DeliveryType.Pickup,
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.Paid,
            Status = OrderStatus.Delivered,
            Total = 9.50m,
            LastStatusChangedAt = DateTime.UtcNow,
            Items =
            [
                new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = orderId,
                    MenuItemId = _seed.ChickenBurger.Id,
                    Name = "Chicken Burger",
                    UnitPrice = 9.50m,
                    Quantity = 1
                }
            ]
        });
        await context.SaveChangesAsync();
        return (connectionString, orderId);
    }
}
