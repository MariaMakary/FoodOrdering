using Client.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Client.Web.Data.Configurations;

public class RatingConfiguration : IEntityTypeConfiguration<Rating>
{
    public void Configure(EntityTypeBuilder<Rating> builder)
    {
        builder.ToTable(t => t.HasCheckConstraint("CK_Ratings_Stars", "\"Stars\" BETWEEN 1 AND 5"));

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Comment).HasMaxLength(1000);

        builder.HasOne(r => r.Order)
            .WithMany(o => o.Ratings)
            .HasForeignKey(r => r.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.MenuItem)
            .WithMany()
            .HasForeignKey(r => r.MenuItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Customer)
            .WithMany()
            .HasForeignKey(r => r.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.OrderId, r.MenuItemId }).IsUnique();
    }
}
