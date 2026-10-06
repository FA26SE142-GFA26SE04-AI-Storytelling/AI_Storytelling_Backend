using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

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

        builder.HasQueryFilter(x => !x.IsDeleted);

        // Buoc 5.6 — goi mac dinh: Personal (49.000d/+50 luot). Goi Organization da bo cung Organization/Teacher.
        builder.HasData(
            new SubscriptionPlan
            {
                Id = 1,
                Name = "Personal",
                PriceVnd = 49000,
                QuotaAmount = 50,
                IsActive = true,
                CreatedAt = SeedCreatedAt,
                IsDeleted = false
            });
    }
}
