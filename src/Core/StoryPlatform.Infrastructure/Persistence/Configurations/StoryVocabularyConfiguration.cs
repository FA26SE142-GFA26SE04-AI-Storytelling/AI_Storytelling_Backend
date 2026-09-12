using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class StoryVocabularyConfiguration : IEntityTypeConfiguration<StoryVocabulary>
{
    public void Configure(EntityTypeBuilder<StoryVocabulary> builder)
    {
        builder.ToTable("story_vocabulary");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Term)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(x => x.Definition)
            .IsRequired()
            .HasColumnType("text");

        builder.HasOne(x => x.StoryVersion)
            .WithMany()
            .HasForeignKey(x => x.StoryVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
