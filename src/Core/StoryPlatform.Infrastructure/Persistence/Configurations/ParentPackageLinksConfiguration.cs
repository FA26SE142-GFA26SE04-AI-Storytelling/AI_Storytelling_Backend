using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public sealed class ParentPackageLinksConfiguration : IEntityTypeConfiguration<StoryVocabulary>, IEntityTypeConfiguration<QuizItem>,
    IEntityTypeConfiguration<ListeningInteraction>, IEntityTypeConfiguration<StoryGenerationJob>,
    IEntityTypeConfiguration<LearningArtifactGenerationBatch>, IEntityTypeConfiguration<MediaContext>,
    IEntityTypeConfiguration<MediaAsset>, IEntityTypeConfiguration<ParentStoryOperation>, IEntityTypeConfiguration<ModerationItem>, IEntityTypeConfiguration<ReadingSession>
{
    private static void Link<T>(EntityTypeBuilder<T> b) where T : class =>
        b.HasOne<ParentStoryPackage>().WithMany().HasForeignKey("ParentStoryPackageId").OnDelete(DeleteBehavior.Restrict);
    public void Configure(EntityTypeBuilder<StoryVocabulary> b) => Link(b);
    public void Configure(EntityTypeBuilder<QuizItem> b) => Link(b);
    public void Configure(EntityTypeBuilder<ListeningInteraction> b) => Link(b);
    public void Configure(EntityTypeBuilder<StoryGenerationJob> b) => Link(b);
    public void Configure(EntityTypeBuilder<LearningArtifactGenerationBatch> b) => Link(b);
    public void Configure(EntityTypeBuilder<MediaContext> b) => Link(b);
    public void Configure(EntityTypeBuilder<MediaAsset> b) => Link(b);
    public void Configure(EntityTypeBuilder<ParentStoryOperation> b) => Link(b);
    public void Configure(EntityTypeBuilder<ModerationItem> b) => Link(b);
    public void Configure(EntityTypeBuilder<ReadingSession> b)
    {
        Link(b);
        b.ToTable("reading_sessions", t => t.HasCheckConstraint("CK_reading_sessions_parent_package_source",
            "(\"ParentStoryPackageId\" IS NULL AND \"FamilyConfirmationId\" IS NULL) OR (\"ParentStoryPackageId\" IS NOT NULL AND \"FamilyConfirmationId\" IS NOT NULL)"));
        b.HasOne<ParentStoryOperation>().WithMany().HasForeignKey(x => x.FamilyConfirmationId).OnDelete(DeleteBehavior.Restrict);
    }
}
