using System.Linq.Expressions;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Application.Features.StoryReview.DTOs;
using StoryPlatform.Application.Features.StoryReview.Services;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class LearningArtifactServiceTests
{
    [Fact]
    public async Task GenerateArtifacts_GeneratesVocabularyQuizAndDiscussion()
    {
        var uow = SeedStoryWithVersion();
        var guard = new MockAccessGuard();
        var aiClient = new MockAIClient();
        var service = new LearningArtifactService(uow, aiClient, guard);

        var result = await service.GenerateArtifactsAsync(1, 1, new GenerateArtifactsRequestDto
        {
            StoryVersionId = 1,
            IncludeVocabulary = true,
            IncludeQuiz = true,
            IncludeDiscussion = true
        });

        Assert.True(result.Success);
        Assert.Equal(1, result.StoryId);
        Assert.Equal(1, result.StoryVersionId);
        Assert.Equal(2, result.VocabularyCount);
        Assert.Equal(3, result.QuizCount);
        Assert.Equal(2, result.DiscussionCount);

        // Verify entities were persisted in UnitOfWork
        var vocabItems = uow.Items<StoryVocabulary>().Where(v => v.StoryVersionId == 1).ToList();
        var quizItems = uow.Items<QuizItem>().Where(q => q.StoryVersionId == 1).ToList();
        var discItems = uow.Items<DiscussionQuestion>().Where(d => d.StoryVersionId == 1).ToList();

        Assert.Equal(2, vocabItems.Count);
        Assert.Equal(3, quizItems.Count);
        Assert.Equal(2, discItems.Count);
    }

    [Fact]
    public async Task GenerateArtifacts_ThrowsNotFound_WhenStoryDoesNotExist()
    {
        var uow = new FakeUnitOfWork();
        var guard = new MockAccessGuard();
        var aiClient = new MockAIClient();
        var service = new LearningArtifactService(uow, aiClient, guard);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GenerateArtifactsAsync(1, 999));
    }

    [Fact]
    public async Task GenerateArtifacts_ThrowsBadRequest_WhenStoryVersionHasNoContent()
    {
        var uow = new FakeUnitOfWork();
        uow.Seed(new Story { Id = 1, ChildProfileId = 1, AuthorUserId = 1, Status = StoryStatus.ContentReview, Language = "vi" });
        uow.Seed(new StoryVersion { Id = 1, StoryId = 1, IsCurrent = true, Content = "" });
        var guard = new MockAccessGuard();
        var aiClient = new MockAIClient();
        var service = new LearningArtifactService(uow, aiClient, guard);

        await Assert.ThrowsAsync<BadRequestException>(() => service.GenerateArtifactsAsync(1, 1));
    }

    private static FakeUnitOfWork SeedStoryWithVersion()
    {
        var uow = new FakeUnitOfWork();
        uow.Seed(new Story
        {
            Id = 1,
            ChildProfileId = 1,
            AuthorUserId = 1,
            Status = StoryStatus.ContentReview,
            Title = "Câu chuyện về chú mèo",
            Language = "vi"
        });
        uow.Seed(new StoryVersion
        {
            Id = 1,
            StoryId = 1,
            VersionNo = 1,
            IsCurrent = true,
            Title = "Chú mèo đi học",
            Content = "Ngày xửa ngày xưa có một chú mèo con rất thích đi học. Mèo con học rất chăm chỉ.",
            Lesson = "Chăm chỉ học tập"
        });
        uow.Seed(new ChildProfile
        {
            Id = 1,
            OwnerUserId = 1,
            Nickname = "Bé Bi",
            AgeBand = AgeBand.Age_6_8,
            Language = "vi",
            Status = ChildProfileStatus.Active
        });
        uow.Seed(new LearningProfile
        {
            Id = 1,
            ChildProfileId = 1,
            ReadingLevel = 2,
            ComprehensionGoal = "Nhận biết nhân vật"
        });
        uow.Seed(new SafetyPolicy
        {
            Id = 1,
            ChildProfileId = 1,
            ComprehensionThresholdPercent = 75m
        });
        return uow;
    }

    private sealed class MockAccessGuard : ISupervisionAccessGuard
    {
        public Task<SupervisionRelationship> EnsureActiveSupervisionAsync(int childProfileId, int userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SupervisionRelationship { ChildProfileId = childProfileId, SupervisorUserId = userId });
        public Task EnsureOwnerAsync(int childProfileId, int userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnsurePermissionAsync(int childProfileId, int userId, Permission permission, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class MockAIClient : IAIStoryGenerationClient
    {
        public Task<GenerateOutlineResponse> GenerateOutlineAsync(GenerateOutlineRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GenerateStoryResponse> GenerateStoryAsync(GenerateStoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RefineStoryResponse> RefineStoryAsync(RefineStoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EvaluateStoryResponse> EvaluateStoryAsync(EvaluateStoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GenerateStoryContentResponse> GenerateStoryContentAsync(GenerateStoryContentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RefineStoryContentResponse> RefineStoryContentAsync(RefineStoryContentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GenerateVocabularyResponse> GenerateVocabularyAsync(GenerateVocabularyRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GenerateVocabularyResponse
            {
                RequestId = request.RequestId,
                Items =
                [
                    new GeneratedVocabularyItemDto("chăm chỉ", "cố gắng làm việc học tập liên tục"),
                    new GeneratedVocabularyItemDto("mèo con", "con mèo còn nhỏ")
                ]
            });

        public Task<GenerateQuizResponse> GenerateQuizAsync(GenerateQuizRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GenerateQuizResponse
            {
                RequestId = request.RequestId,
                Items =
                [
                    new Contracts.AI.Models.QuizItemDto { Type = "multiple_choice", Question = "Mèo con thích làm gì?", Options = ["Đi học", "Đi chơi"], CorrectAnswer = "Đi học" },
                    new Contracts.AI.Models.QuizItemDto { Type = "true_false", Question = "Mèo con rất lười?", CorrectAnswer = "false" },
                    new Contracts.AI.Models.QuizItemDto { Type = "short_answer", Question = "Bài học là gì?", CorrectAnswer = "Chăm chỉ" }
                ]
            });

        public Task<GenerateDiscussionResponse> GenerateDiscussionAsync(GenerateDiscussionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GenerateDiscussionResponse
            {
                RequestId = request.RequestId,
                Items =
                [
                    new DiscussionQuestionDto("Em có thích chú mèo chăm chỉ không?"),
                    new DiscussionQuestionDto("Em sẽ làm gì để học tập tốt hơn?")
                ]
            });

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
}
