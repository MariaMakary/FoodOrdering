using Client.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Client.Web.Data.Configurations;

public class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ToTable(t => t.HasCheckConstraint("CK_CartItems_Quantity", "\"Quantity\" > 0"));

        builder.HasKey(i => i.Id);

        builder.HasOne(i => i.Cart)
            .WithMany(c => c.Items)
            .HasForeignKey(i => i.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.MenuItem)
            .WithMany()
            .HasForeignKey(i => i.MenuItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
