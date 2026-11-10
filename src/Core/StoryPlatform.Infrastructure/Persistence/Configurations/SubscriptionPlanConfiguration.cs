using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class SubscriptionPlanConfiguration : IEntityTypeConfiguration<SubscriptionPlan>
{
    // Fixed value (not DateTime.UtcNow) so the generated migration is reproducible across builds.
    private static readonly DateTime SeedCreatedAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<SubscriptionPlan> builder)
    {
        builder.ToTable("subscription_plans");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(x => x.PlanType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(PlanType.Legacy)
            .IsRequired();

        builder.HasQueryFilter(x => !x.IsDeleted);

        // Luong 2 — goi Personal cu (49.000d/+50 luot) ngung ban, giu lai de giao dich cu hien dung ten.
        // Mo hinh moi: Goi Doc, Goi Sang tao theo chu ky thang va Add-on.
        builder.HasData(
            new SubscriptionPlan
            {
                Id = 1,
                Name = "Personal",
                PlanType = PlanType.Legacy,
                PriceVnd = 49000,
                AiCredits = 50,
                IsActive = false,
                CreatedAt = SeedCreatedAt,
                IsDeleted = false
            },
            new SubscriptionPlan
            {
                Id = 2,
                Name = "Gói Đọc",
                PlanType = PlanType.Reading,
                PriceVnd = 49000,
                AiCredits = 0,
                IsActive = true,
                CreatedAt = SeedCreatedAt,
                IsDeleted = false
            },
            new SubscriptionPlan
            {
                Id = 3,
                Name = "Gói Sáng tạo",
                PlanType = PlanType.Creative,
                PriceVnd = 99000,
                AiCredits = 4,
                IsActive = true,
                CreatedAt = SeedCreatedAt,
                IsDeleted = false
            },
            new SubscriptionPlan
            {
                Id = 4,
                Name = "Add-on",
                PlanType = PlanType.AddOn,
                PriceVnd = 29000,
                AiCredits = 2,
                IsActive = true,
                CreatedAt = SeedCreatedAt,
                IsDeleted = false
            });
    }
}
