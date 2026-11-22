using Client.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Client.Web.Data.Configurations;

public class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.HasKey(c => c.Id);

        builder.HasOne(c => c.Customer)
            .WithOne()
            .HasForeignKey<Cart>(c => c.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => c.CustomerId).IsUnique();
    }
}
