using MaarifPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaarifPlatform.Infrastructure.Persistence.Configurations;

public class ReviewQueueItemConfiguration : IEntityTypeConfiguration<ReviewQueueItem>
{
    public void Configure(EntityTypeBuilder<ReviewQueueItem> b)
    {
        b.ToTable("review_queue");
        b.HasKey(e => e.Id);
        b.Property(e => e.Status).HasMaxLength(30).IsRequired();
        b.Property(e => e.ReasonFlagsJson).HasColumnType("jsonb");
        // §hitl: kuyruk önceliklendirme sorgusu bu bileşik indekse dayanır.
        b.HasIndex(e => new { e.Status, e.Priority });

        b.HasOne(e => e.AssignedToUser)
            .WithMany()
            .HasForeignKey(e => e.AssignedToUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
