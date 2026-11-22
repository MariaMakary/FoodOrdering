using Client.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Client.Web.Data.Configurations;

public class OrderItemExtraConfiguration : IEntityTypeConfiguration<OrderItemExtra>
{
    public void Configure(EntityTypeBuilder<OrderItemExtra> builder)
    {
        builder.ToTable(t => t.HasCheckConstraint("CK_OrderItemExtras_Price", "\"Price\" >= 0"));

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();

        builder.HasOne(e => e.OrderItem)
            .WithMany(i => i.Extras)
            .HasForeignKey(e => e.OrderItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
