using Client.Web.Models.Entities;
using Client.Web.Models.Enums;
using Client.Web.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Enums;
using Shared.Persistence;

namespace Client.Web.Data;

public class ClientDbContext(DbContextOptions<ClientDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    public const string OrderNumberSequence = "OrderNumbers";

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<MenuItemExtra> MenuItemExtras => Set<MenuItemExtra>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<CartItemExtra> CartItemExtras => Set<CartItemExtra>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderItemExtra> OrderItemExtras => Set<OrderItemExtra>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Rating> Ratings => Set<Rating>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();
    public DbSet<MessageLog> MessageLogs => Set<MessageLog>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(AuditInterceptor.Instance);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>().ToTable("Users");
        modelBuilder.Entity<ApplicationRole>().ToTable("Roles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");

        modelBuilder.HasSequence<long>(OrderNumberSequence).StartsAt(1000);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ClientDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(10, 2);

        configurationBuilder.Properties<OrderStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<PaymentStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<DeliveryType>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<PaymentMethod>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<PaymentAttemptStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<MessageDirection>().HaveConversion<string>().HaveMaxLength(10);
        configurationBuilder.Properties<MessageLogStatus>().HaveConversion<string>().HaveMaxLength(20);
    }
}
