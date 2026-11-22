using Client.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Client.Web.Data.Configurations;

public class CartItemExtraConfiguration : IEntityTypeConfiguration<CartItemExtra>
{
    public void Configure(EntityTypeBuilder<CartItemExtra> builder)
    {
        builder.HasKey(e => new { e.CartItemId, e.MenuItemExtraId });

        builder.HasOne(e => e.CartItem)
            .WithMany(i => i.Extras)
            .HasForeignKey(e => e.CartItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.MenuItemExtra)
            .WithMany()
            .HasForeignKey(e => e.MenuItemExtraId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
