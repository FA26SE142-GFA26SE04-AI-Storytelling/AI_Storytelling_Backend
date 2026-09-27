using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class ChildSessionConfiguration : IEntityTypeConfiguration<ChildSession>
{
    public const string ExactlyOneEntrySourceConstraintName = "CK_child_sessions_exactly_one_entry_source";

    // Cùng quy tắc với reading_sessions để Bước 3.1 chép nguyên nguồn lối vào sang phiên đọc.
    public const string ExactlyOneEntrySourceSql =
        "(\"ChildAccessCredentialId\" IS NOT NULL AND \"SupervisorSessionId\" IS NULL) OR "
        + "(\"ChildAccessCredentialId\" IS NULL AND \"SupervisorSessionId\" IS NOT NULL)";

    public void Configure(EntityTypeBuilder<ChildSession> builder)
    {
        builder.ToTable("child_sessions", table =>
            table.HasCheckConstraint(ExactlyOneEntrySourceConstraintName, ExactlyOneEntrySourceSql));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.SessionKey)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasOne(x => x.ChildProfile)
            .WithMany()
            .HasForeignKey(x => x.ChildProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ChildAccessCredential)
            .WithMany()
            .HasForeignKey(x => x.ChildAccessCredentialId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.SupervisorSession)
            .WithMany()
            .HasForeignKey(x => x.SupervisorSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.SessionKey)
            .IsUnique();

        builder.HasIndex(x => x.ChildProfileId);
        builder.HasIndex(x => x.ChildAccessCredentialId);
        builder.HasIndex(x => x.SupervisorSessionId);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
