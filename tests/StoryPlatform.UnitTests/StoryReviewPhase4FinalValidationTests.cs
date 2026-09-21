using System.Linq.Expressions;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.ExistingStories.DTOs;
using StoryPlatform.Application.Features.ExistingStories.Interfaces;
using StoryPlatform.Application.Features.ExistingStories.Services;
using StoryPlatform.Application.Features.StoryReview.Interfaces;
using StoryPlatform.Application.Features.StoryReview.Services;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class StoryReviewPhase4FinalValidationTests
{
    [Fact]
    public async Task Validate_Fails_WhenSafetyScoreBelowThreshold()
    {
        var uow = SeedContentReviewWithAllArtifacts();
        var version = uow.Items<StoryVersion>().Single();
        version.SafetyScore = 60m;
        uow.Seed(new SafetyPolicy
        {
            Id = 1,
            ChildProfileId = 1,
            SafetyScoreThreshold = 80m
        });
        var service = BuildReviewService(uow);

        var result = await service.ValidateAsync(1, 1);

        Assert.False(result.CanApprove);
        var safetyCheck = result.Checks.First(c => c.Name == "safety_passed");
        Assert.False(safetyCheck.Passed);
        Assert.Contains(result.Issues, i => i.Contains("thấp hơn ngưỡng"));
    }

    [Fact]
    public async Task Validate_Fails_WhenBlockedCategoryTermPresent()
    {
        var uow = SeedContentReviewWithAllArtifacts();
        var version = uow.Items<StoryVersion>().Single();
        version.Content += " trong một trận đánh nhau dữ dội.";
        uow.Seed(new SafetyPolicy
        {
            Id = 1,
            ChildProfileId = 1
        });
        uow.Seed(new ContentCategory
        {
            Id = 10,
            Code = "VIOLENCE",
            DisplayName = "đánh nhau",
            IsActive = true
        });
        uow.Seed(new SafetyPolicyCategory
        {
            Id = 1,
            SafetyPolicyId = 1,
            ContentCategoryId = 10,
            Rule = PolicyRule.Blocked
        });
        var service = BuildReviewService(uow);

        var result = await service.ValidateAsync(1, 1);

        Assert.False(result.CanApprove);
        var safetyCheck = result.Checks.First(c => c.Name == "safety_passed");
        Assert.False(safetyCheck.Passed);
        Assert.Contains(result.Issues, i => i.Contains("blocked term"));
    }

    [Fact]
    public async Task Validate_Fails_WhenExistingStoryEvaluationIsBlocked()
    {
        var uow = SeedContentReviewWithAllArtifacts();
        var story = uow.Items<Story>().Single();
        story.Source = StorySource.Manual; // Luồng 2 existing story
        var cache = new InMemoryExistingStoryEvaluationCache();
        cache.Set(new ExistingStoryEvaluationDto
        {
            StoryId = 1,
            StoryVersionId = 1,
            Decision = ExistingStoryDecision.Blocked,
            CanKeepOriginal = false
        });
        var service = BuildReviewService(uow, evaluationCache: cache);

        var result = await service.ValidateAsync(1, 1);

        Assert.False(result.CanApprove);
        var profileCheck = result.Checks.First(c => c.Name == "profile_decision_resolved");
        Assert.False(profileCheck.Passed);
        Assert.Contains(result.Issues, i => i.Contains("Existing story evaluation decision is Blocked"));
    }

    [Fact]
    public async Task ReviewCompletion_Tracking_MarksComponentsReviewed()
    {
        var uow = SeedContentReviewWithAllArtifacts();
        var store = new InMemoryReviewCompletionStore();
        var service = BuildReviewService(uow, reviewStore: store);

        Assert.False(store.IsReviewCompleted(1, 1));

        await service.CompleteStoryReviewAsync(1, 1);
        await service.CompleteVocabularyReviewAsync(1, 1);
        await service.CompleteQuizReviewAsync(1, 1);
        await service.CompleteDiscussionReviewAsync(1, 1);

        Assert.True(store.IsReviewCompleted(1, 1));
    }

    [Fact]
    public async Task AutoPublish_Succeeds_WhenThresholdMetAndParentalGateDisabled()
    {
        var uow = SeedContentReviewWithAllArtifacts();
        var version = uow.Items<StoryVersion>().Single();
        version.SafetyScore = 95m;
        uow.Seed(new SafetyPolicy
        {
            Id = 1,
            ChildProfileId = 1,
            ParentalGateEnabled = false,
            RequiredApprovalMode = ApprovalMode.AutoPublishOnThreshold,
            SafetyScoreThreshold = 80m
        });
        var service = BuildReviewService(uow);

        var result = await service.EvaluateAndApplyAutoPublishAsync(1);

        Assert.NotNull(result);
        Assert.Equal(StoryStatus.Approved.ToString(), result.Status);
        var updatedStory = uow.Items<Story>().Single(s => s.Id == 1);
        Assert.Equal(StoryStatus.Approved, updatedStory.Status);
        Assert.Contains(uow.Items<StoryGenerationJob>(), j => j.Operation == GenerationJobOperation.GenerateMediaPackage);
    }

    [Fact]
    public async Task AutoPublish_ReturnsNull_WhenParentalGateEnabled()
    {
        var uow = SeedContentReviewWithAllArtifacts();
        var version = uow.Items<StoryVersion>().Single();
        version.SafetyScore = 95m;
        uow.Seed(new SafetyPolicy
        {
            Id = 1,
            ChildProfileId = 1,
            ParentalGateEnabled = true, // Forces manual review
            RequiredApprovalMode = ApprovalMode.AutoPublishOnThreshold,
            SafetyScoreThreshold = 80m
        });
        var service = BuildReviewService(uow);

        var result = await service.EvaluateAndApplyAutoPublishAsync(1);

        Assert.Null(result);
        var story = uow.Items<Story>().Single(s => s.Id == 1);
        Assert.Equal(StoryStatus.ContentReview, story.Status);
    }

    [Fact]
    public async Task AutoPublish_ReturnsNull_WhenApprovalModeIsNotAutoPublish()
    {
        var uow = SeedContentReviewWithAllArtifacts();
        var version = uow.Items<StoryVersion>().Single();
        version.SafetyScore = 95m;
        uow.Seed(new SafetyPolicy
        {
            Id = 1,
            ChildProfileId = 1,
            ParentalGateEnabled = false,
            RequiredApprovalMode = ApprovalMode.AlwaysManual,
            SafetyScoreThreshold = 80m
        });
        var service = BuildReviewService(uow);

        var result = await service.EvaluateAndApplyAutoPublishAsync(1);

        Assert.Null(result);
        var story = uow.Items<Story>().Single(s => s.Id == 1);
        Assert.Equal(StoryStatus.ContentReview, story.Status);
    }

    #region Fixtures

    private static StoryReviewService BuildReviewService(
        FakeUnitOfWork uow,
        IReviewCompletionStore? reviewStore = null,
        IExistingStoryEvaluationCache? evaluationCache = null)
    {
        return new StoryReviewService(
            uow,
            new FakeAIClient(),
            new InMemoryProposalCache(),
            reviewStore,
            evaluationCache);
    }

    private static FakeUnitOfWork SeedContentReviewWithAllArtifacts()
    {
        var uow = new FakeUnitOfWork();
        uow.Seed(new UserAccount { Id = 1, Role = UserRole.Parent, Status = AccountStatus.LoggedIn });
        uow.Seed(new Story
        {
            Id = 1,
            ChildProfileId = 1,
            AuthorUserId = 1,
            Source = StorySource.Ai,
            Status = StoryStatus.ContentReview,
            Language = "vi",
            ReadingLevel = 2
        });
        uow.Seed(new SupervisionRelationship
        {
            Id = 1,
            ChildProfileId = 1,
            SupervisorUserId = 1,
            SupervisorRole = SupervisorRole.Owner
        });
        uow.Seed(new StoryVersion
        {
            Id = 1,
            StoryId = 1,
            VersionNo = 1,
            IsCurrent = true,
            Title = "Cáo nhỏ tốt bụng",
            Content = "Đây là nội dung truyện mới trong rừng. Cáo nhỏ có chuyến phiêu lưu cùng bạn bè.",
            Lesson = "Biết chia sẻ với bạn bè",
            SafetyScore = 90m
        });
        // 5 Vocabulary items that appear in content
        uow.Seed(new StoryVocabulary { Id = 1, StoryVersionId = 1, Term = "rừng", Definition = "Nơi có nhiều cây" });
        uow.Seed(new StoryVocabulary { Id = 2, StoryVersionId = 1, Term = "phiêu lưu", Definition = "Đi chơi xa khám phá" });
        uow.Seed(new StoryVocabulary { Id = 3, StoryVersionId = 1, Term = "bạn bè", Definition = "Người thân thiết chơi cùng" });
        uow.Seed(new StoryVocabulary { Id = 4, StoryVersionId = 1, Term = "nội dung", Definition = "Điều được kể" });
        uow.Seed(new StoryVocabulary { Id = 5, StoryVersionId = 1, Term = "chuyến", Definition = "Một lần đi xa" });

        // 3 Quiz items (all types)
        uow.Seed(new QuizItem { Id = 1, StoryVersionId = 1, Type = QuizType.MultipleChoice, Question = "Hai bạn đi đâu?", Choices = "[\"Khu rừng\",\"Bãi biển\"]", CorrectAnswer = "Khu rừng" });
        uow.Seed(new QuizItem { Id = 2, StoryVersionId = 1, Type = QuizType.TrueFalse, Question = "Chuyến đi rất vui?", CorrectAnswer = "true" });
        uow.Seed(new QuizItem { Id = 3, StoryVersionId = 1, Type = QuizType.ShortAnswer, Question = "Bài học là gì?", CorrectAnswer = "Tình bạn" });

        // 2 Discussion items
        uow.Seed(new DiscussionQuestion { Id = 1, StoryVersionId = 1, Question = "Em có thích khu rừng không?", IsMoralLesson = true });
        uow.Seed(new DiscussionQuestion { Id = 2, StoryVersionId = 1, Question = "Em sẽ giúp đỡ bạn bè như thế nào?" });

        return uow;
    }

    private sealed class FakeAIClient : IAIStoryGenerationClient
    {
        public Task<GenerateOutlineResponse> GenerateOutlineAsync(GenerateOutlineRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GenerateStoryResponse> GenerateStoryAsync(GenerateStoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RefineStoryResponse> RefineStoryAsync(RefineStoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EvaluateStoryResponse> EvaluateStoryAsync(EvaluateStoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GenerateStoryContentResponse> GenerateStoryContentAsync(GenerateStoryContentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RefineStoryContentResponse> RefineStoryContentAsync(RefineStoryContentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GenerateVocabularyResponse> GenerateVocabularyAsync(GenerateVocabularyRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GenerateQuizResponse> GenerateQuizAsync(GenerateQuizRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GenerateDiscussionResponse> GenerateDiscussionAsync(GenerateDiscussionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EvaluateContentSafetyResponse> EvaluateContentSafetyAsync(EvaluateContentSafetyRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        private readonly Dictionary<Type, object> _repositories = [];
        public IGenericRepository<T> Repository<T>() where T : class =>
            (IGenericRepository<T>)(_repositories.TryGetValue(typeof(T), out var r) ? r : _repositories[typeof(T)] = new FakeRepo<T>());
        public void Seed<T>(T item) where T : class => ((FakeRepo<T>)Repository<T>()).Items.Add(item);
        public IReadOnlyList<T> Items<T>() where T : class => ((FakeRepo<T>)Repository<T>()).Items;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AcquireTransactionLockAsync(int resourceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeRepo<T> : IGenericRepository<T> where T : class
    {
        public List<T> Items { get; } = [];
        public Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(item => item is BaseEntity entity && entity.Id == Convert.ToInt32(id)));
        public Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<T>>(Items);
        public Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, string? includeProperties = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>(Items.Where(predicate.Compile()).ToArray());
        public Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, string? includeProperties = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(predicate.Compile()));
        public Task<(IReadOnlyList<T> Items, int TotalCount)> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<T, bool>>? filter = null, Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null, string? includeProperties = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<T> AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            if (entity is BaseEntity b && b.Id == 0)
            {
                b.Id = Items.OfType<BaseEntity>().Select(x => x.Id).DefaultIfEmpty().Max() + 1;
            }
            Items.Add(entity);
            return Task.FromResult(entity);
        }
        public Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
        {
            foreach (var e in entities) AddAsync(e, cancellationToken);
            return Task.CompletedTask;
        }
        public void Update(T entity) { }
        public void Delete(T entity) => Items.Remove(entity);
        public void DeleteRange(IEnumerable<T> entities) => Items.Clear();
        public Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(predicate is null ? Items.Count : Items.Count(predicate.Compile()));
        public Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Any(predicate.Compile()));
        public IQueryable<T> Query() => Items.AsQueryable();
    }

    #endregion
}
