using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FoodOrdering.Tests.Helpers;

public class TestDbFactoryTests
{
    [Fact]
    public async Task Create_CalledTwice_ReturnsSeparateEmptyDatabases()
    {
        await using var first = TestDbFactory.Create<SampleDbContext>();
        first.Items.Add(new SampleItem { Name = "Burger" });
        await first.SaveChangesAsync();

        await using var second = TestDbFactory.Create<SampleDbContext>();

        (await first.Items.CountAsync()).Should().Be(1);
        (await second.Items.CountAsync()).Should().Be(0);
    }

    public class SampleDbContext(DbContextOptions<SampleDbContext> options) : DbContext(options)
    {
        public DbSet<SampleItem> Items => Set<SampleItem>();
    }

    public class SampleItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
