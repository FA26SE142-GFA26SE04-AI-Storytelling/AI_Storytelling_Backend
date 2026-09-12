using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class TelemetryLogConfiguration : IEntityTypeConfiguration<TelemetryLog>
{
    public void Configure(EntityTypeBuilder<TelemetryLog> builder)
    {
        builder.ToTable("telemetry_logs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.EventType)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(x => x.EventPayload)
            .HasColumnType("text");

        builder.HasOne(x => x.ReadingSession)
            .WithMany()
            .HasForeignKey(x => x.ReadingSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
