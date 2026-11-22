using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Shared.Persistence.Outbox;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).HasMaxLength(100).IsRequired();
        builder.Property(m => m.Payload).HasColumnType("text").IsRequired();
        builder.Property(m => m.LastError).HasColumnType("text");

        builder.HasIndex(m => new { m.SentAt, m.CreatedAt });
    }
}
