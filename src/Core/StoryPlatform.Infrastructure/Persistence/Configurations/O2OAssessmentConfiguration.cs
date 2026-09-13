using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations;

public class O2OAssessmentConfiguration : IEntityTypeConfiguration<O2OAssessment>
{
    public void Configure(EntityTypeBuilder<O2OAssessment> builder)
    {
        builder.ToTable("o2o_assessments");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Notes)
            .HasColumnType("text");

        builder.HasOne(x => x.AssignmentRecipient)
            .WithMany()
            .HasForeignKey(x => x.AssignmentRecipientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.TeacherUser)
            .WithMany()
            .HasForeignKey(x => x.TeacherUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
