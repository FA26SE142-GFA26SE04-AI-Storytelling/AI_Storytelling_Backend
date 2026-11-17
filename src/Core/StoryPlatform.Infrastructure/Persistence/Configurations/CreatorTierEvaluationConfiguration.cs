using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class CreatorTierEvaluationConfiguration : IEntityTypeConfiguration<CreatorTierEvaluation>
{
    public void Configure(EntityTypeBuilder<CreatorTierEvaluation> builder)
    {
        builder.ToTable("creator_tier_evaluations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CompletionRate).HasPrecision(5, 4);

        builder.Property(x => x.TierBefore)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.TierAfter)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasOne(x => x.Creator)
            .WithMany()
            .HasForeignKey(x => x.CreatorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Mỗi Creator chỉ được xét một lần mỗi tháng: worker và lần cấp hạn mức chạy cùng lúc không tạo hai dòng.
        builder.HasIndex(x => new { x.CreatorUserId, x.PeriodMonth })
            .IsUnique()
            .HasFilter("NOT \"IsDeleted\"");

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
