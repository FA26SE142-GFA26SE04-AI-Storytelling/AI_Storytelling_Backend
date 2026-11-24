using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class ReadingScheduleWindowConfiguration : IEntityTypeConfiguration<ReadingScheduleWindow>
{
    public void Configure(EntityTypeBuilder<ReadingScheduleWindow> builder)
    {
        builder.ToTable("reading_schedule_windows");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.DayType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasOne(x => x.ReadingSchedule)
            .WithMany()
            .HasForeignKey(x => x.ReadingScheduleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.ReadingScheduleId, x.DayType, x.Slot })
            .IsUnique();

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
