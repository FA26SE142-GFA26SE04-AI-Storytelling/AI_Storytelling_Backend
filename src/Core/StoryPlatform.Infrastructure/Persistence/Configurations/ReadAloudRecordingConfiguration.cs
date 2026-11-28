using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class ReadAloudRecordingConfiguration : IEntityTypeConfiguration<ReadAloudRecording>
{
    public const string OneTargetConstraintName = "CK_read_aloud_recordings_one_target";

    public void Configure(EntityTypeBuilder<ReadAloudRecording> builder)
    {
        builder.ToTable("read_aloud_recordings", table => table.HasCheckConstraint(OneTargetConstraintName,
            "(\"StorySegmentId\" IS NOT NULL AND \"ListeningInteractionId\" IS NULL) OR "
            + "(\"StorySegmentId\" IS NULL AND \"ListeningInteractionId\" IS NOT NULL)"));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AudioStoragePath).HasMaxLength(500).IsRequired();
        builder.Property(x => x.AudioContentType).HasMaxLength(50).IsRequired();

        builder.HasOne(x => x.ReadingSession).WithMany().HasForeignKey(x => x.ReadingSessionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.StorySegment).WithMany().HasForeignKey(x => x.StorySegmentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ListeningInteraction).WithMany().HasForeignKey(x => x.ListeningInteractionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Mỗi phiên: một bản ghi sống cho mỗi đoạn truyện / mỗi đoạn luyện (lần đọc gần nhất).
        builder.HasIndex(x => new { x.ReadingSessionId, x.StorySegmentId })
            .IsUnique()
            .HasFilter("\"StorySegmentId\" IS NOT NULL AND \"IsDeleted\" = false")
            .HasDatabaseName("UX_read_aloud_recordings_session_segment");
        // Đọc luân phiên có một bản ghi cho mỗi lượt thoại; các đoạn luyện khác để lượt = NULL (NULL tính là một giá trị).
        builder.HasIndex(x => new { x.ReadingSessionId, x.ListeningInteractionId, x.ListeningInteractionOptionId })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasFilter("\"ListeningInteractionId\" IS NOT NULL AND \"IsDeleted\" = false")
            .HasDatabaseName("UX_read_aloud_recordings_session_practice");
        builder.HasIndex(x => x.ChildProfileId).HasDatabaseName("IX_read_aloud_recordings_child");

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
