using Admin.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Admin.Web.Data.Configurations;

public class MenuItemExtraConfiguration : IEntityTypeConfiguration<MenuItemExtra>
{
    public void Configure(EntityTypeBuilder<MenuItemExtra> builder)
    {
        builder.ToTable(t => t.HasCheckConstraint("CK_MenuItemExtras_Price", "\"Price\" >= 0"));

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();

        builder.HasOne(e => e.MenuItem)
            .WithMany(m => m.Extras)
            .HasForeignKey(e => e.MenuItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
