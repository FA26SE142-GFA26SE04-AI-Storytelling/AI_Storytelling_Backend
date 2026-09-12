using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class VocabularyNotebookEntryConfiguration : IEntityTypeConfiguration<VocabularyNotebookEntry>
{
    public void Configure(EntityTypeBuilder<VocabularyNotebookEntry> builder)
    {
        builder.ToTable("vocabulary_notebook_entries");

        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.ChildProfile)
            .WithMany()
            .HasForeignKey(x => x.ChildProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.StoryVocabulary)
            .WithMany()
            .HasForeignKey(x => x.StoryVocabularyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.ChildProfileId, x.StoryVocabularyId })
            .IsUnique();

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
