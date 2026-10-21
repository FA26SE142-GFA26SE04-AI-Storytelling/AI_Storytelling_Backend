using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public sealed class ModerationRubricConfiguration : IEntityTypeConfiguration<ModerationRubricVersion>
{
    public void Configure(EntityTypeBuilder<ModerationRubricVersion> b)
    {
        b.ToTable("moderation_rubric_versions", t => t.HasCheckConstraint("CK_rubric_revision", "\"Revision\" > 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(40).IsRequired();
        b.Property(x => x.VersionNo).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.SchemaVersion).HasMaxLength(60).IsRequired();
        b.Property(x => x.DefinitionJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.HasIndex(x => new { x.Code, x.VersionNo }).IsUnique().HasDatabaseName("UX_moderation_rubric_version");
        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UX_moderation_rubric_active").HasFilter("\"Status\" = 'Active' AND NOT \"IsDeleted\"");
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.ActivatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

public sealed class ModerationRubricAuditConfiguration : IEntityTypeConfiguration<ModerationRubricAuditLog>
{
    public void Configure(EntityTypeBuilder<ModerationRubricAuditLog> b)
    {
        b.ToTable("moderation_rubric_audit_logs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Action).HasMaxLength(40).IsRequired();
        b.Property(x => x.BeforeJson).HasColumnType("jsonb");
        b.Property(x => x.AfterJson).HasColumnType("jsonb");
        b.HasOne(x => x.RubricVersion).WithMany().HasForeignKey(x => x.RubricVersionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.RubricVersionId, x.CreatedAt });
    }
}
