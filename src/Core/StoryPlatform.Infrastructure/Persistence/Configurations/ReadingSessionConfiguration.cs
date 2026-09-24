using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class ReadingSessionConfiguration : IEntityTypeConfiguration<ReadingSession>
{
    public const string ExactlyOneEntrySourceConstraintName = "CK_reading_sessions_exactly_one_entry_source";

    // Đúng 1 trong 2: trẻ tự vào (avatar+PIN/QR) HOẶC Supervisor bàn giao thiết bị — không thiếu, không cả hai.
    public const string ExactlyOneEntrySourceSql =
        "(\"ChildAccessCredentialId\" IS NOT NULL AND \"SupervisorSessionId\" IS NULL) OR "
        + "(\"ChildAccessCredentialId\" IS NULL AND \"SupervisorSessionId\" IS NOT NULL)";

    public void Configure(EntityTypeBuilder<ReadingSession> builder)
    {
        builder.ToTable("reading_sessions", table =>
            table.HasCheckConstraint(ExactlyOneEntrySourceConstraintName, ExactlyOneEntrySourceSql));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.HasOne(x => x.ChildProfile)
            .WithMany()
            .HasForeignKey(x => x.ChildProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Story)
            .WithMany()
            .HasForeignKey(x => x.StoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.AssignmentRecipient)
            .WithMany()
            .HasForeignKey(x => x.AssignmentRecipientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.StoryVersion)
            .WithMany()
            .HasForeignKey(x => x.StoryVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ChildAccessCredential)
            .WithMany()
            .HasForeignKey(x => x.ChildAccessCredentialId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.SupervisorSession)
            .WithMany()
            .HasForeignKey(x => x.SupervisorSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
