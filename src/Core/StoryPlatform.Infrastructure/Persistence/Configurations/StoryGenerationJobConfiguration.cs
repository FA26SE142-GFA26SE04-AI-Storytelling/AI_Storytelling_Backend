using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class StoryGenerationJobConfiguration : IEntityTypeConfiguration<StoryGenerationJob>
{
    public void Configure(EntityTypeBuilder<StoryGenerationJob> builder)
    {
        builder.ToTable("story_generation_jobs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Stage)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.GuardrailResult)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(x => x.FallbackMessage)
            .HasColumnType("text");

        builder.HasOne(x => x.Story)
            .WithMany()
            .HasForeignKey(x => x.StoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.PromptCatalogVersion)
            .WithMany()
            .HasForeignKey(x => x.PromptCatalogVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
