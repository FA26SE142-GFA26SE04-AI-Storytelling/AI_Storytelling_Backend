using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Action)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(x => x.EntityType)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(x => x.BeforeState).HasColumnType("text");
        builder.Property(x => x.AfterState).HasColumnType("text");

        builder.HasOne(x => x.ActorUser)
            .WithMany()
            .HasForeignKey(x => x.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Màn audit log của Administrator lọc theo thời gian, action, đối tượng và người làm; mới nhất trước.
        builder.HasIndex(x => x.OccurredAt).HasDatabaseName("IX_audit_logs_OccurredAt");
        builder.HasIndex(x => new { x.Action, x.OccurredAt }).HasDatabaseName("IX_audit_logs_Action_OccurredAt");
        builder.HasIndex(x => new { x.EntityType, x.EntityId }).HasDatabaseName("IX_audit_logs_EntityType_EntityId");
        builder.HasIndex(x => new { x.ActorUserId, x.OccurredAt }).HasDatabaseName("IX_audit_logs_ActorUserId_OccurredAt");

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
