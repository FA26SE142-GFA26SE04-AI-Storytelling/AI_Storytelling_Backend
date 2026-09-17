using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public sealed class MediaContextConfiguration : IEntityTypeConfiguration<MediaContext>
{
    public void Configure(EntityTypeBuilder<MediaContext> builder)
    {
        builder.ToTable("media_contexts", table =>
            table.HasCheckConstraint("CK_media_contexts_revision", "\"Revision\" > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ContextJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(x => new { x.StoryVersionId, x.Revision }).IsUnique();
        builder.HasOne(x => x.StoryVersion).WithMany().HasForeignKey(x => x.StoryVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
