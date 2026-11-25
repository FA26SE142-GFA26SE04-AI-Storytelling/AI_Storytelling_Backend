using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Configurations.Events;

public class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("events");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(220).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.BannerUrl).HasMaxLength(1000);
        builder.Property(x => x.TargetAgeBands).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.ConfigVersion).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Format).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.ScoringMode).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.HasOne(x => x.MainCategory).WithMany().HasForeignKey(x => x.MainCategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.Slug).IsUnique().HasFilter("NOT \"IsDeleted\"");
        builder.HasIndex(x => x.Status);
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}

public class EventModeratorAssignmentConfiguration : IEntityTypeConfiguration<EventModeratorAssignment>
{
    public void Configure(EntityTypeBuilder<EventModeratorAssignment> builder)
    {
        builder.ToTable("event_moderator_assignments");
        builder.HasKey(assignment => assignment.Id);
        builder.HasOne<Event>().WithMany().HasForeignKey(assignment => assignment.EventId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(assignment => assignment.ModeratorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(assignment => new { assignment.EventId, assignment.ModeratorId }).IsUnique();
    }
}

public class EventTrackConfiguration : IEntityTypeConfiguration<EventTrack>
{
    public void Configure(EntityTypeBuilder<EventTrack> builder)
    {
        builder.ToTable("event_tracks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TrackType).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasOne(x => x.Event).WithMany(e => e.Tracks).HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.EventId, x.TrackType }).IsUnique().HasFilter("NOT \"IsDeleted\"");
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}

public class EventActivityConfiguration : IEntityTypeConfiguration<EventActivity>
{
    public void Configure(EntityTypeBuilder<EventActivity> builder)
    {
        builder.ToTable("event_activities");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ActivityType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.AllowedAudience).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.StorySource).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.HasOne(x => x.Event).WithMany(e => e.Activities).HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.EventId, x.ActivityType }).IsUnique().HasFilter("NOT \"IsDeleted\"");
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}

public class EventRuleVersionConfiguration : IEntityTypeConfiguration<EventRuleVersion>
{
    public void Configure(EntityTypeBuilder<EventRuleVersion> builder)
    {
        builder.ToTable("event_rule_versions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CompletionMode).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.HasOne(x => x.Event).WithMany(e => e.RuleVersions).HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.EventId, x.VersionNo }).IsUnique().HasFilter("NOT \"IsDeleted\"");
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}

public class EventTaskDefinitionConfiguration : IEntityTypeConfiguration<EventTaskDefinition>
{
    public void Configure(EntityTypeBuilder<EventTaskDefinition> builder)
    {
        builder.ToTable("event_task_definitions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TaskCode).HasMaxLength(40).IsRequired();
        builder.Property(x => x.TargetTrackType).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.VerifierType).HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.HasOne(x => x.RuleVersion).WithMany(r => r.TaskDefinitions).HasForeignKey(x => x.EventRuleVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.EventRuleVersionId, x.TaskCode }).IsUnique().HasFilter("NOT \"IsDeleted\"");
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}

public class EventParticipationConfiguration : IEntityTypeConfiguration<EventParticipation>
{
    public void Configure(EntityTypeBuilder<EventParticipation> builder)
    {
        builder.ToTable("event_participations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Track).WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Activity).WithMany().HasForeignKey(x => x.EventActivityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.EventId, x.TrackId, x.UserId }).IsUnique().HasFilter("\"TrackId\" IS NOT NULL AND NOT \"IsDeleted\"");
        builder.HasIndex(x => new { x.EventId, x.EventActivityId, x.UserId }).IsUnique().HasFilter("\"EventActivityId\" IS NOT NULL AND NOT \"IsDeleted\"");
        builder.HasIndex(x => new { x.UserId, x.Status });
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}

public class EventSubmissionConfiguration : IEntityTypeConfiguration<EventSubmission>
{
    public void Configure(EntityTypeBuilder<EventSubmission> builder)
    {
        builder.ToTable("event_submissions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Participation).WithMany().HasForeignKey(x => x.ParticipationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Activity).WithMany().HasForeignKey(x => x.EventActivityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.EventId, x.CreatorUserId }).IsUnique().HasFilter("\"EventActivityId\" IS NULL AND NOT \"IsDeleted\"");
        builder.HasIndex(x => new { x.EventId, x.StoryId }).IsUnique().HasFilter("NOT \"IsDeleted\"");
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}

public class EventStoryConfiguration : IEntityTypeConfiguration<EventStory>
{
    public void Configure(EntityTypeBuilder<EventStory> builder)
    {
        builder.ToTable("event_stories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Source).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.EventId, x.StoryId }).IsUnique().HasFilter("NOT \"IsDeleted\"");
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}

public class EventTaskProgressConfiguration : IEntityTypeConfiguration<EventTaskProgress>
{
    public void Configure(EntityTypeBuilder<EventTaskProgress> builder)
    {
        builder.ToTable("event_task_progress");
        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.Participation).WithMany().HasForeignKey(x => x.ParticipationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.TaskDefinition).WithMany().HasForeignKey(x => x.TaskDefinitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ParticipationId, x.TaskDefinitionId });
        builder.HasIndex(x => new { x.ParticipationId, x.ActivityLocalDate });
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}

public class EventAppealConfiguration : IEntityTypeConfiguration<EventAppeal>
{
    public void Configure(EntityTypeBuilder<EventAppeal> builder)
    {
        builder.ToTable("event_appeals");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ReasonText).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.ResolutionNote).HasMaxLength(2000);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsRequired();

        builder.HasOne(x => x.Participation).WithMany().HasForeignKey(x => x.ParticipationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ParticipationId)
            .IsUnique()
            .HasDatabaseName("IX_event_appeals_ActivePerParticipation")
            .HasFilter("\"Status\" IN ('Open', 'UnderReview', 'Escalated') AND NOT \"IsDeleted\"");
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}

public class EventEvaluationConfiguration : IEntityTypeConfiguration<EventEvaluation>
{
    public void Configure(EntityTypeBuilder<EventEvaluation> builder)
    {
        builder.ToTable("event_evaluations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TotalWeightedScore).HasPrecision(6, 2);
        builder.Property(x => x.Comment).HasMaxLength(2000);

        builder.HasOne(x => x.Submission).WithMany().HasForeignKey(x => x.EventSubmissionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.EventSubmissionId, x.EvaluatorUserId }).IsUnique().HasFilter("NOT \"IsDeleted\"");
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}

public class EventAwardConfiguration : IEntityTypeConfiguration<EventAward>
{
    public void Configure(EntityTypeBuilder<EventAward> builder)
    {
        builder.ToTable("event_awards");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RankOrTier).HasMaxLength(64).IsRequired();
        builder.Property(x => x.RewardName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RewardType).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.ResultSource).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.TargetAudience).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Activity).WithMany().HasForeignKey(x => x.EventActivityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.EventId, x.TrackId, x.RankOrTier, x.SlotIndex })
            .IsUnique()
            .HasDatabaseName("IX_event_awards_ActiveSlot")
            .HasFilter("\"TrackId\" IS NOT NULL AND \"Status\" NOT IN ('Expired', 'UnclaimedBudgetReturned', 'Failed') AND NOT \"IsDeleted\"");
        builder.HasIndex(x => new { x.EventId, x.EventActivityId, x.RankOrTier, x.SlotIndex })
            .IsUnique()
            .HasDatabaseName("IX_event_awards_ActiveActivitySlot")
            .HasFilter("\"EventActivityId\" IS NOT NULL AND \"Status\" NOT IN ('Expired', 'UnclaimedBudgetReturned', 'Failed') AND NOT \"IsDeleted\"");
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}

public class RewardFulfillmentConfiguration : IEntityTypeConfiguration<RewardFulfillment>
{
    public void Configure(EntityTypeBuilder<RewardFulfillment> builder)
    {
        builder.ToTable("reward_fulfillments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TrackingOrBankTxCode).HasMaxLength(120);
        builder.Property(x => x.FailureReason).HasMaxLength(1000);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsRequired();

        builder.HasOne(x => x.Award).WithMany().HasForeignKey(x => x.EventAwardId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.EventAwardId, x.AttemptNo }).IsUnique().HasFilter("NOT \"IsDeleted\"");
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}

public class EventVoteConfiguration : IEntityTypeConfiguration<EventVote>
{
    public void Configure(EntityTypeBuilder<EventVote> builder)
    {
        builder.ToTable("event_votes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Comment).HasMaxLength(2000);
        builder.Property(x => x.DisqualifiedReason).HasMaxLength(1000);

        builder.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Submission).WithMany().HasForeignKey(x => x.EventSubmissionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Participation).WithMany().HasForeignKey(x => x.ParticipationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.EventId, x.StoryId, x.ParentUserId }).IsUnique().HasFilter("NOT \"IsDeleted\"");
        builder.HasIndex(x => new { x.EventId, x.EventSubmissionId, x.IsDisqualified });
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
