using Admin.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Admin.Web.Data.Configurations;

public class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedMessage> builder)
    {
        builder.HasKey(p => p.MessageId);
        builder.Property(p => p.MessageId).ValueGeneratedNever();
        builder.Property(p => p.Type).HasMaxLength(100).IsRequired();
    }
}
