using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class AiGovernanceMetricConfiguration : IEntityTypeConfiguration<AiGovernanceMetric>
{
    public void Configure(EntityTypeBuilder<AiGovernanceMetric> builder)
    {
        builder.ToTable("ai_governance_metrics");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.GenerationSuccessRate).HasColumnType("decimal(6,3)");
        builder.Property(x => x.SafetyFlagRate).HasColumnType("decimal(6,3)");
        builder.Property(x => x.RegenerationRate).HasColumnType("decimal(6,3)");
        builder.Property(x => x.ApprovalRate).HasColumnType("decimal(6,3)");

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
