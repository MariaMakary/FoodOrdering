using Admin.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Admin.Web.Data.Configurations;

public class MessageLogConfiguration : IEntityTypeConfiguration<MessageLog>
{
    public void Configure(EntityTypeBuilder<MessageLog> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Type).HasMaxLength(100).IsRequired();
        builder.Property(m => m.Queue).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Payload).HasColumnType("text").IsRequired();
        builder.Property(m => m.Error).HasColumnType("text");

        builder.HasIndex(m => m.MessageId);
        builder.HasIndex(m => m.CreatedAt);
    }
}
