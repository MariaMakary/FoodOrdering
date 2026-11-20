using Microsoft.EntityFrameworkCore;

namespace FoodOrdering.Tests.Helpers;

public static class TestDbFactory
{
    public static TContext Create<TContext>() where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return (TContext)Activator.CreateInstance(typeof(TContext), options)!;
    }
}
