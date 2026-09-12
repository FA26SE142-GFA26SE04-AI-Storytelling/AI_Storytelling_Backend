using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<Story> Stories => Set<Story>();

    // Child profile & supervision
    public DbSet<ChildProfile> ChildProfiles => Set<ChildProfile>();
    public DbSet<LearningProfile> LearningProfiles => Set<LearningProfile>();
    public DbSet<LearningProfileTopic> LearningProfileTopics => Set<LearningProfileTopic>();
    public DbSet<ContentCategory> ContentCategories => Set<ContentCategory>();
    public DbSet<SafetyPolicy> SafetyPolicies => Set<SafetyPolicy>();
    public DbSet<SafetyPolicyCategory> SafetyPolicyCategories => Set<SafetyPolicyCategory>();
    public DbSet<SupervisionInvitation> SupervisionInvitations => Set<SupervisionInvitation>();
    public DbSet<SupervisionRelationship> SupervisionRelationships => Set<SupervisionRelationship>();
    public DbSet<SupervisionPermission> SupervisionPermissions => Set<SupervisionPermission>();

    // Classroom / organization
    public DbSet<ClassGroup> ClassGroups => Set<ClassGroup>();
    public DbSet<ClassGroupMember> ClassGroupMembers => Set<ClassGroupMember>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();
    public DbSet<OrganizationPermission> OrganizationPermissions => Set<OrganizationPermission>();
    public DbSet<OrgConsentRecord> OrgConsentRecords => Set<OrgConsentRecord>();
    public DbSet<OrgSafetyPolicyTemplate> OrgSafetyPolicyTemplates => Set<OrgSafetyPolicyTemplate>();
    public DbSet<OrgSafetyPolicyCategory> OrgSafetyPolicyCategories => Set<OrgSafetyPolicyCategory>();

    // Story content & generation
    public DbSet<StoryCategory> StoryCategories => Set<StoryCategory>();
    public DbSet<StoryVersion> StoryVersions => Set<StoryVersion>();
    public DbSet<StoryGenerationJob> StoryGenerationJobs => Set<StoryGenerationJob>();
    public DbSet<StoryGenerationRequest> StoryGenerationRequests => Set<StoryGenerationRequest>();
    public DbSet<StoryVocabulary> StoryVocabularies => Set<StoryVocabulary>();
    public DbSet<QuizItem> QuizItems => Set<QuizItem>();
    public DbSet<DiscussionQuestion> DiscussionQuestions => Set<DiscussionQuestion>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<PromptCatalogVersion> PromptCatalogVersions => Set<PromptCatalogVersion>();

    // Reading & engagement
    public DbSet<ReadingSession> ReadingSessions => Set<ReadingSession>();
    public DbSet<ReadingProgress> ReadingProgresses => Set<ReadingProgress>();
    public DbSet<TelemetryLog> TelemetryLogs => Set<TelemetryLog>();
    public DbSet<QuizAttempt> QuizAttempts => Set<QuizAttempt>();
    public DbSet<Achievement> Achievements => Set<Achievement>();
    public DbSet<Badge> Badges => Set<Badge>();
    public DbSet<VocabularyNotebookEntry> VocabularyNotebookEntries => Set<VocabularyNotebookEntry>();

    // Assignments & sharing
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<AssignmentRecipient> AssignmentRecipients => Set<AssignmentRecipient>();
    public DbSet<SharedStory> SharedStories => Set<SharedStory>();
    public DbSet<SharedStoryRecipient> SharedStoryRecipients => Set<SharedStoryRecipient>();
    public DbSet<O2OAssessment> O2OAssessments => Set<O2OAssessment>();

    // Learning intelligence & governance
    public DbSet<InterventionCase> InterventionCases => Set<InterventionCase>();
    public DbSet<AiGovernanceMetric> AiGovernanceMetrics => Set<AiGovernanceMetric>();
    public DbSet<BusinessReport> BusinessReports => Set<BusinessReport>();
    public DbSet<DataRequest> DataRequests => Set<DataRequest>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<LearningInsight> LearningInsights => Set<LearningInsight>();
    public DbSet<Recommendation> Recommendations => Set<Recommendation>();
    public DbSet<RecommendationReview> RecommendationReviews => Set<RecommendationReview>();
    public DbSet<ChildProfileVersionHistory> ChildProfileVersionHistories => Set<ChildProfileVersionHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Tự động áp dụng tất cả các cấu hình IEntityTypeConfiguration trong assembly hiện tại
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = DateTime.UtcNow;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    break;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
