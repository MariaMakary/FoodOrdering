using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace FoodOrdering.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public class PostgresFixtureTests(PostgresFixture postgres)
{
    [Fact]
    public async Task CreateDbContextAsync_ShouldReturnSeparateEmptyDatabases_WhenCalledTwice()
    {
        await using SampleDbContext first = await postgres.CreateDbContextAsync<SampleDbContext>();
        first.Items.Add(new SampleItem { Name = "Burger" });
        await first.SaveChangesAsync();

        await using SampleDbContext second = await postgres.CreateDbContextAsync<SampleDbContext>();

        (await first.Items.CountAsync()).ShouldBe(1);
        (await second.Items.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldThrow_WhenAUniqueIndexIsViolated()
    {
        await using SampleDbContext context = await postgres.CreateDbContextAsync<SampleDbContext>();
        context.Items.Add(new SampleItem { Name = "Burger" });
        context.Items.Add(new SampleItem { Name = "Burger" });

        await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    public class SampleDbContext(DbContextOptions<SampleDbContext> options) : DbContext(options)
    {
        public DbSet<SampleItem> Items => Set<SampleItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<SampleItem>().HasIndex(item => item.Name).IsUnique();
    }

    public class SampleItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
