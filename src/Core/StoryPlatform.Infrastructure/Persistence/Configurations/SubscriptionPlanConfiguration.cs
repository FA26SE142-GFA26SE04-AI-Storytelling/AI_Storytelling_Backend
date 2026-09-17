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

        builder.Property(x => x.ApplicableScope)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasQueryFilter(x => !x.IsDeleted);

        // Buoc 5.6 — 2 goi mac dinh da chot: Personal (49.000d/+50 luot) va Organization (99.000d/+150 luot).
        builder.HasData(
            new SubscriptionPlan
            {
                Id = 1,
                Name = "Personal",
                ApplicableScope = ProfileScope.Personal,
                PriceVnd = 49000,
                QuotaAmount = 50,
                IsActive = true,
                CreatedAt = SeedCreatedAt,
                IsDeleted = false
            },
            new SubscriptionPlan
            {
                Id = 2,
                Name = "Organization",
                ApplicableScope = ProfileScope.Organization,
                PriceVnd = 99000,
                QuotaAmount = 150,
                IsActive = true,
                CreatedAt = SeedCreatedAt,
                IsDeleted = false
            });
    }
}
