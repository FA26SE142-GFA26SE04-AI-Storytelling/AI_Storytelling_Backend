using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class CreatorEarningPeriodConfiguration : IEntityTypeConfiguration<CreatorEarningPeriod>
{
    public void Configure(EntityTypeBuilder<CreatorEarningPeriod> builder)
    {
        builder.ToTable("creator_earning_periods");

        builder.HasKey(x => x.Id);

        // Mỗi tháng chốt một lần: hai lượt worker chạy cùng lúc không cộng thu nhập hai lần.
        builder.HasIndex(x => x.PeriodMonth)
            .IsUnique()
            .HasFilter("NOT \"IsDeleted\"");

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
