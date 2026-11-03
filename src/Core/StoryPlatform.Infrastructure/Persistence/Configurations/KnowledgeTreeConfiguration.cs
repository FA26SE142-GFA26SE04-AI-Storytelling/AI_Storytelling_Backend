using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class KnowledgeTreeConfiguration : IEntityTypeConfiguration<KnowledgeTree>
{
    public void Configure(EntityTypeBuilder<KnowledgeTree> builder)
    {
        builder.ToTable("knowledge_trees", table =>
        {
            table.HasCheckConstraint("CK_knowledge_trees_ExpInvested", "\"ExpInvested\" >= 0");
            table.HasCheckConstraint("CK_knowledge_trees_CurrentTreeExp", "\"CurrentTreeExp\" >= 0");
            table.HasCheckConstraint("CK_knowledge_trees_FloweredTreeCount", "\"FloweredTreeCount\" >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.ChildProfile)
            .WithMany()
            .HasForeignKey(x => x.ChildProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ChildProfileId)
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
