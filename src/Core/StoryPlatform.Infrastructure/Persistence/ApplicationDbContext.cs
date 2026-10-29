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
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Story> Stories => Set<Story>();
    public DbSet<ParentStoryOperation> ParentStoryOperations => Set<ParentStoryOperation>();

    // Child profile & supervision
    public DbSet<ChildProfile> ChildProfiles => Set<ChildProfile>();
    public DbSet<LearningProfile> LearningProfiles => Set<LearningProfile>();
    public DbSet<LearningProfileTopic> LearningProfileTopics => Set<LearningProfileTopic>();
    public DbSet<ContentCategory> ContentCategories => Set<ContentCategory>();
    public DbSet<WishCard> WishCards => Set<WishCard>();
    public DbSet<StoryWishCardMatch> StoryWishCardMatches => Set<StoryWishCardMatch>();
    public DbSet<StoryWishSuggestion> StoryWishSuggestions => Set<StoryWishSuggestion>();
    public DbSet<StoryWish> StoryWishes => Set<StoryWish>();
    public DbSet<SafetyPolicy> SafetyPolicies => Set<SafetyPolicy>();
    public DbSet<SafetyPolicyCategory> SafetyPolicyCategories => Set<SafetyPolicyCategory>();
    public DbSet<ChildAccessCredential> ChildAccessCredentials => Set<ChildAccessCredential>();
    public DbSet<ChildSession> ChildSessions => Set<ChildSession>();

    // Story content & generation
    public DbSet<StoryCategory> StoryCategories => Set<StoryCategory>();
    public DbSet<StoryVersion> StoryVersions => Set<StoryVersion>();
    public DbSet<StoryAuthoringOperation> StoryAuthoringOperations => Set<StoryAuthoringOperation>();
    public DbSet<StoryAssistanceTask> StoryAssistanceTasks => Set<StoryAssistanceTask>();
    public DbSet<StoryAssistanceStep> StoryAssistanceSteps => Set<StoryAssistanceStep>();
    public DbSet<StoryGenerationJob> StoryGenerationJobs => Set<StoryGenerationJob>();
    public DbSet<StoryGenerationRequest> StoryGenerationRequests => Set<StoryGenerationRequest>();
    public DbSet<StoryVocabulary> StoryVocabularies => Set<StoryVocabulary>();
    public DbSet<QuizItem> QuizItems => Set<QuizItem>();
    public DbSet<DiscussionQuestion> DiscussionQuestions => Set<DiscussionQuestion>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<MediaAssetGenerationTask> MediaAssetGenerationTasks => Set<MediaAssetGenerationTask>();
    public DbSet<LearningArtifactGenerationBatch> LearningArtifactGenerationBatches => Set<LearningArtifactGenerationBatch>();
    public DbSet<StoryScene> StoryScenes => Set<StoryScene>();
    public DbSet<IllustrationBeat> IllustrationBeats => Set<IllustrationBeat>();
    public DbSet<StorySegment> StorySegments => Set<StorySegment>();
    public DbSet<MediaContext> MediaContexts => Set<MediaContext>();
    public DbSet<PromptCatalogVersion> PromptCatalogVersions => Set<PromptCatalogVersion>();
    public DbSet<AuthoringGuideCatalog> AuthoringGuideCatalogs => Set<AuthoringGuideCatalog>();
    public DbSet<AuthoringGuideCatalogVersion> AuthoringGuideCatalogVersions => Set<AuthoringGuideCatalogVersion>();
    public DbSet<ModerationRubricVersion> ModerationRubricVersions => Set<ModerationRubricVersion>();
    public DbSet<ModerationRubricAuditLog> ModerationRubricAuditLogs => Set<ModerationRubricAuditLog>();
    public DbSet<ContentReport> ContentReports => Set<ContentReport>();
    public DbSet<ModerationItem> ModerationItems => Set<ModerationItem>();
    public DbSet<ModerationReviewView> ModerationReviewViews => Set<ModerationReviewView>();
    public DbSet<ReadingRoadmap> ReadingRoadmaps => Set<ReadingRoadmap>();
    public DbSet<ReadingRoadmapItem> ReadingRoadmapItems => Set<ReadingRoadmapItem>();
    public DbSet<ChildRoadmapItem> ChildRoadmapItems => Set<ChildRoadmapItem>();

    // Reading & engagement
    public DbSet<ReadingSession> ReadingSessions => Set<ReadingSession>();
    public DbSet<ReadingProgress> ReadingProgresses => Set<ReadingProgress>();
    public DbSet<TelemetryLog> TelemetryLogs => Set<TelemetryLog>();
    public DbSet<QuizAttempt> QuizAttempts => Set<QuizAttempt>();
    public DbSet<Achievement> Achievements => Set<Achievement>();
    public DbSet<Badge> Badges => Set<Badge>();
    public DbSet<VocabularyNotebookEntry> VocabularyNotebookEntries => Set<VocabularyNotebookEntry>();

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
    public DbSet<TokenQuotaConfig> TokenQuotaConfigs => Set<TokenQuotaConfig>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Tự động áp dụng tất cả các cấu hình IEntityTypeConfiguration trong assembly hiện tại
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Owned metadata shares the version lifetime; keep its denormalized story index key authoritative.
        foreach (var version in ChangeTracker.Entries<StoryVersion>())
            version.Entity.AuthoringMetadata.StoryId = version.Entity.StoryId;
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
