using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class StoryVersionConfiguration : IEntityTypeConfiguration<StoryVersion>
{
    public void Configure(EntityTypeBuilder<StoryVersion> builder)
    {
        builder.ToTable("story_versions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.EditType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.OutlineOpening).HasColumnType("text");
        builder.Property(x => x.OutlineDevelopment).HasColumnType("text");
        builder.Property(x => x.OutlineEnding).HasColumnType("text");
        builder.Property(x => x.Content).HasColumnType("text");
        builder.Property(x => x.Lesson).HasColumnType("text");

        builder.Property(x => x.ReadabilityFkgl).HasColumnType("decimal(6,3)");
        builder.Property(x => x.ReadabilityFre).HasColumnType("decimal(6,3)");
        builder.Property(x => x.SafetyScore).HasColumnType("decimal(6,3)");

        builder.HasOne(x => x.Story)
            .WithMany()
            .HasForeignKey(x => x.StoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.EditorUser)
            .WithMany()
            .HasForeignKey(x => x.EditorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
