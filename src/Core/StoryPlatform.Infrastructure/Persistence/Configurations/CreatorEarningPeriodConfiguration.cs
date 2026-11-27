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

        // Các kỳ nối tiếp nhau nên mỗi ngày đầu kỳ chỉ có một dòng: hai lượt chốt cùng lúc không cộng thu nhập hai lần.
        builder.HasIndex(x => x.PeriodStart)
            .IsUnique()
            .HasFilter("NOT \"IsDeleted\"");

        builder.ToTable(table => table.HasCheckConstraint("CK_creator_earning_periods_range",
            "\"PeriodEnd\" >= \"PeriodStart\""));

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
