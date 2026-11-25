using Client.Web.Data;
using Client.Web.Models.Entities;
using Client.Web.Models.Identity;

namespace FoodOrdering.IntegrationTests.Client;

public sealed class ClientMenuSeed
{
    public ApplicationUser Sara { get; } = NewCustomer("sara@example.com", "Sara", "Haddad", "Hamra Street 12, Beirut");
    public ApplicationUser Omar { get; } = NewCustomer("omar@example.com", "Omar", "Khoury", null);
    public Category Burgers { get; } = new() { Id = Guid.NewGuid(), Name = "Burgers", SortOrder = 1 };
    public MenuItem ChickenBurger { get; }
    public MenuItem BeefBurger { get; }
    public MenuItem Lemonade { get; }
    public MenuItem OldBurger { get; }
    public MenuItemExtra Cheese { get; }
    public MenuItemExtra Bacon { get; }
    public MenuItemExtra Pickles { get; }

    public ClientMenuSeed()
    {
        ChickenBurger = NewDish("Chicken Burger", 9.50m);
        Cheese = new MenuItemExtra { Id = Guid.NewGuid(), MenuItemId = ChickenBurger.Id, Name = "Extra cheese", Price = 1.00m };
        Bacon = new MenuItemExtra { Id = Guid.NewGuid(), MenuItemId = ChickenBurger.Id, Name = "Bacon", Price = 1.50m };
        ChickenBurger.Extras = [Cheese, Bacon];

        BeefBurger = NewDish("Beef Burger", 11.00m);
        Pickles = new MenuItemExtra { Id = Guid.NewGuid(), MenuItemId = BeefBurger.Id, Name = "Pickles", Price = 0.50m };
        BeefBurger.Extras = [Pickles];

        Lemonade = NewDish("Lemonade", 3.00m);

        OldBurger = NewDish("Old Burger", 5.00m);
        OldBurger.IsDeleted = true;
    }

    public async Task SaveAsync(ClientDbContext context)
    {
        context.Users.AddRange(Sara, Omar);
        context.Categories.Add(Burgers);
        context.MenuItems.AddRange(ChickenBurger, BeefBurger, Lemonade, OldBurger);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private MenuItem NewDish(string name, decimal price) => new()
    {
        Id = Guid.NewGuid(),
        CategoryId = Burgers.Id,
        Name = name,
        Price = price,
        IsAvailable = true,
        SourceUpdatedAt = DateTime.UtcNow
    };

    private static ApplicationUser NewCustomer(string email, string firstName, string lastName, string? address) => new()
    {
        Id = Guid.NewGuid(),
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        FirstName = firstName,
        LastName = lastName,
        PhoneNumber = "+961 70 123 456",
        DefaultAddress = address
    };
}
