using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class StoryCategoryConfiguration : IEntityTypeConfiguration<StoryCategory>
{
    public void Configure(EntityTypeBuilder<StoryCategory> builder)
    {
        builder.ToTable("story_categories");

        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.Story)
            .WithMany()
            .HasForeignKey(x => x.StoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ContentCategory)
            .WithMany()
            .HasForeignKey(x => x.ContentCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.StoryId, x.ContentCategoryId })
            .IsUnique();

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
