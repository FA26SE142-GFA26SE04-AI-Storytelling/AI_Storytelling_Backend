using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class PlanSettingsConfiguration : IEntityTypeConfiguration<PlanSettings>
{
    private static readonly DateTime SeedCreatedAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<PlanSettings> builder)
    {
        builder.ToTable("plan_settings", table =>
        {
            table.HasCheckConstraint("CK_plan_settings_fund_percent",
                "\"ReadingFundPercent\" BETWEEN 0 AND 100 AND \"CreativeFundPercent\" BETWEEN 0 AND 100");
            table.HasCheckConstraint("CK_plan_settings_min_read_seconds",
                "\"MinimumReadSeconds\" BETWEEN 1 AND 3600");
        });

        builder.HasKey(x => x.Id);
        builder.HasQueryFilter(x => !x.IsDeleted);

        // Dòng cấu hình duy nhất, giá trị mặc định theo Luồng 5 (5a).
        builder.HasData(new PlanSettings { Id = PlanSettings.SingletonId, CreatedAt = SeedCreatedAt });
    }
}
