using Admin.Web.Data;
using Admin.Web.Models.Entities;
using FoodOrdering.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace FoodOrdering.IntegrationTests.Persistence;

[Collection(PostgresCollection.Name)]
public class AuditInterceptorTests(PostgresFixture postgres)
{
    [Fact]
    public async Task SaveChangesAsync_ShouldSetCreatedAtAndUpdatedAt_WhenARowIsAdded()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        var category = new Category { Id = Guid.NewGuid(), Name = "Burgers", SortOrder = 1 };

        context.Categories.Add(category);
        await context.SaveChangesAsync();

        category.CreatedAt.ShouldBe(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        category.UpdatedAt.ShouldBe(category.CreatedAt);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldMoveOnlyUpdatedAt_WhenARowIsChanged()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        var category = new Category { Id = Guid.NewGuid(), Name = "Burgers", SortOrder = 1 };
        await using (AdminDbContext setup = postgres.NewDbContext<AdminDbContext>(connectionString))
        {
            setup.Categories.Add(category);
            await setup.SaveChangesAsync();
        }

        await using (AdminDbContext staff = postgres.NewDbContext<AdminDbContext>(connectionString))
        {
            Category saved = await staff.Categories.SingleAsync(c => c.Id == category.Id);
            saved.Name = "Grill";
            saved.CreatedAt = DateTime.UtcNow.AddYears(-1);
            await staff.SaveChangesAsync();
        }

        await using AdminDbContext check = postgres.NewDbContext<AdminDbContext>(connectionString);
        Category changed = await check.Categories.SingleAsync(c => c.Id == category.Id);
        changed.CreatedAt.ShouldBe(category.CreatedAt, TimeSpan.FromMilliseconds(1));
        changed.UpdatedAt.ShouldBeGreaterThan(changed.CreatedAt);
    }
}
