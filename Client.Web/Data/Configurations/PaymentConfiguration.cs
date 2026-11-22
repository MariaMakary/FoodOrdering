using Client.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Client.Web.Data.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable(t => t.HasCheckConstraint("CK_Payments_Amount", "\"Amount\" >= 0"));

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Provider).HasMaxLength(20).IsRequired();
        builder.Property(p => p.ProviderSessionId).HasMaxLength(200).IsRequired();

        builder.HasOne(p => p.Order)
            .WithOne(o => o.Payment)
            .HasForeignKey<Payment>(p => p.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.HasIndex(p => p.ProviderSessionId).IsUnique();
    }
}
