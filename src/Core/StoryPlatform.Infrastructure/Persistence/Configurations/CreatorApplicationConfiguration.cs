using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class CreatorApplicationConfiguration : IEntityTypeConfiguration<CreatorApplication>
{
    public void Configure(EntityTypeBuilder<CreatorApplication> builder)
    {
        builder.ToTable("creator_applications");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Introduction)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(x => x.PortfolioUrl)
            .HasMaxLength(500);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.RejectionReason)
            .HasMaxLength(500);

        builder.HasOne(x => x.Applicant)
            .WithMany()
            .HasForeignKey(x => x.ApplicantUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ReviewedBy)
            .WithMany()
            .HasForeignKey(x => x.ReviewedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ApplicantUserId, "IX_creator_applications_ApplicantUserId");
        builder.HasIndex(x => new { x.Status, x.CreatedAt });
        // Mỗi tài khoản chỉ có một đơn đang chờ: hai lần nộp lại cùng lúc không tạo hai đơn Pending.
        builder.HasIndex(x => x.ApplicantUserId, "UX_creator_applications_pending_per_applicant")
            .IsUnique()
            .HasFilter($"\"Status\" = '{nameof(CreatorApplicationStatus.Pending)}' AND NOT \"IsDeleted\"");

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
