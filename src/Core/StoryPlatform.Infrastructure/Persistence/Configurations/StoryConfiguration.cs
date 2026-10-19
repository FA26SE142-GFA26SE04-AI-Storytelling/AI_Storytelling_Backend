using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class StoryConfiguration : IEntityTypeConfiguration<Story>
{
    public void Configure(EntityTypeBuilder<Story> builder)
    {
        builder.ToTable("stories", t => t.HasCheckConstraint("CK_Story_ScopeChild",
            "(\"Scope\" = 'Parent' AND \"ChildProfileId\" IS NOT NULL AND \"ChildProfileId\" > 0) OR (\"Scope\" = 'Creator' AND \"ChildProfileId\" IS NULL)"));

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Scope)
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(StoryPlatform.Domain.Enums.StoryScope.Parent)
            .HasSentinel(StoryPlatform.Domain.Enums.StoryScope.Parent)
            .IsRequired();
        builder.Property(s => s.AuthoringRevisionToken).HasMaxLength(32).IsRequired().IsConcurrencyToken();
        builder.Property(s => s.ExperienceMode).HasMaxLength(20).IsRequired();
        builder.Property(s => s.WritingPurpose).HasMaxLength(30);
        builder.Property(s => s.CreateOperationId).HasMaxLength(100);
        builder.Property(s => s.CreateRequestHash).HasMaxLength(64);
        builder.Property(s => s.CreateResponseJson).HasColumnType("text");
        builder.HasIndex(s => new { s.AuthorUserId, s.CreateOperationId }).IsUnique()
            .HasFilter("\"CreateOperationId\" IS NOT NULL");

        builder.Property(s => s.Title)
            .HasMaxLength(200);

        builder.Property(s => s.Description)
            .HasMaxLength(1000);

        builder.Property(s => s.AgeBand)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(s => s.VocabularyLevel)
            .HasMaxLength(20);

        builder.Property(s => s.Genre)
            .HasMaxLength(50);

        builder.Property(s => s.MoralLesson)
            .HasMaxLength(500);

        builder.Property(s => s.Language)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(s => s.CoverImageUrl)
            .HasMaxLength(500);

        builder.Property(s => s.Source)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(s => s.OutputMode)
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(StoryPlatform.Domain.Enums.StoryOutputMode.Learning)
            .HasSentinel(StoryPlatform.Domain.Enums.StoryOutputMode.Learning)
            .IsRequired();

        builder.Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(s => s.ArchivedReason)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(s => s.EverGrayZone).HasDefaultValue(false);

        // Foreign key to UserAccount (Author)
        builder.HasOne(s => s.Author)
            .WithMany(u => u.Stories)
            .HasForeignKey(s => s.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Foreign key to ChildProfile (owner of the story)
        builder.HasOne(s => s.ChildProfile)
            .WithMany()
            .HasForeignKey(s => s.ChildProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        // Soft delete global filter
        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}
