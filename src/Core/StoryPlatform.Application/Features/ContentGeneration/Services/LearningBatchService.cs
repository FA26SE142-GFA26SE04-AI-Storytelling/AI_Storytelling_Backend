using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AIStoryInput.Models;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Application.Features.ContentGeneration.Interfaces;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ContentGeneration.Services;

public sealed class LearningBatchService(IUnitOfWork uow, ILearningBatchRunner runner, ISupervisionAccessGuard guard,
    LearningBatchOptions options) : ILearningBatchService
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<LearningBatchPackage> GenerateAsync(StoryGenerationJob root, string token, StoryVersion version,
        AIStoryInputContextSnapshot context, CancellationToken ct)
    {
        var profile = context.LearningProfile ?? options.Profile(context.AgeBand);
        var catalogue = root.PromptCatalogVersionId is int id
            ? await uow.Repository<PromptCatalogVersion>().FirstOrDefaultAsync(x => x.Id == id, cancellationToken: ct)
            : (await uow.Repository<PromptCatalogVersion>().FindAsync(x => x.GradeBand == context.AgeBand &&
                x.Status == CatalogStatus.Published && !x.IsDeleted, cancellationToken: ct)).OrderByDescending(x => x.EffectiveDate).FirstOrDefault();
        if (catalogue is null) throw new ConflictException("LEARNING_PROMPT_CATALOG_REQUIRED");
        var snapshot = new AiGenerationSnapshot
        {
            PromptCatalogVersionId = catalogue.Id, PromptVersionNo = catalogue.VersionNo + ":learning-grounded/v1",
            Config = new AiGenerationConfigSnapshot { MaxVocabularyItems = profile.VocabularyTarget, MaxQuizItems = profile.QuizTarget },
            Templates = new Dictionary<string, string>
            {
                ["Vocabulary"] = "Create child-friendly vocabulary grounded only in the supplied story. Use its language, age band and reading level. Follow the structured batch contract. Context JSON: {{context}}",
                ["Quiz"] = "Create child-friendly learning questions grounded only in the supplied story. Use its language and age band. Explain answers using evidence. Follow the structured batch contract. Context JSON: {{context}}"
            }
        };
        var storyDto = new StoryContentDto { Title = version.Title, Lesson = version.Lesson ?? "",
            StorySections = [new(1, "", version.Content!)] };
        var vocab = new GenerateVocabularyRequest { Story = storyDto, AgeBand = context.AgeBand, Language = context.Language,
            ReadingLevel = context.ReadingLevel.ToString(), VocabularyLevel = context.VocabularyLevel, Snapshot = snapshot,
            Batch = new() { Kind = "vocabulary", TargetCount = profile.VocabularyTarget } };
        GenerateQuizRequest Quiz(string kind, int count, IReadOnlyList<GeneratedVocabularyItemDto>? terms = null,
            IReadOnlyList<string>? excluded = null) => new()
        {
            Story = storyDto, AgeBand = context.AgeBand, Language = context.Language, Snapshot = snapshot,
            ComprehensionGoal = context.ComprehensionGoal, Vocabulary = terms ?? [],
            Batch = new() { Kind = kind, TargetCount = count, ExcludedQuestions = excluded ?? [],
                RequiredQuestionTypes = LearningBatchSpec.QuizTypesForBatch(kind, count) }
        };
        var initial = new[]
        {
            new LearningBatchRequest(vocab, null, context),
            new LearningBatchRequest(null, Quiz("quiz_facts", (profile.QuizTarget + 2) / 3), context),
            new LearningBatchRequest(null, Quiz("quiz_reasoning", (profile.QuizTarget + 1) / 3), context)
        };
        var ids = await QueueAsync(root, token, version, initial, ct);
        await runner.RunAsync(ids, token, ct);
        var completed = await RowsAsync(root.Id, ct, ["vocabulary", "quiz_facts", "quiz_reasoning"]);
        var words = Deserialize<GenerateVocabularyResponse>(completed.Single(x => x.Kind == "vocabulary").ResultJson);
        var previous = completed.Where(x => x.Kind.StartsWith("quiz_", StringComparison.Ordinal)).SelectMany(x => Deserialize<GenerateQuizResponse>(x.ResultJson).Items)
            .Select(x => x.Question).ToArray();
        // This dependency is explicit: story-only quizzes need no invented vocabulary input.
        ids = await QueueAsync(root, token, version,
            [new(null, Quiz("quiz_vocabulary", profile.QuizTarget / 3, words.Items, previous), context)], ct);
        await runner.RunAsync(ids, token, ct);
        return Merge(await RowsAsync(root.Id, ct));
    }

    private async Task<IReadOnlyList<int>> QueueAsync(StoryGenerationJob observed, string token, StoryVersion version,
        IReadOnlyList<LearningBatchRequest> requests, CancellationToken ct)
    {
        await uow.BeginTransactionAsync(ct);
        try
        {
            await uow.AcquireTransactionLockAsync(observed.StoryId, ct);
            var root = await uow.Repository<StoryGenerationJob>().FirstOrDefaultAsync(x => x.Id == observed.Id, cancellationToken: ct);
            var story = await uow.Repository<Story>().FirstOrDefaultAsync(x => x.Id == observed.StoryId, cancellationToken: ct) ?? throw new ConflictException("STALE_LEARNING_SOURCE");
            var fresh = await uow.Repository<StoryVersion>().FirstOrDefaultAsync(x => x.Id == version.Id, cancellationToken: ct) ?? throw new ConflictException("STALE_LEARNING_SOURCE");
            if (root?.Status != GenerationJobStatus.Processing || root.ConcurrencyToken != token || root.StoryVersionId != version.Id || root.LeaseExpiresAt <= DateTime.UtcNow)
                throw new ConflictException("STALE_LEARNING_JOB");
            var actor = root.RequestedByUserId ?? story.AuthorUserId;
            var contextHash = await LearningBatchAccess.AuthorizeAsync(uow, guard, story, fresh, actor, ct);
            var source = LearningBatchAccess.Source(story, fresh);
            if (source != LearningBatchAccess.Source(story, version)) throw new ConflictException("STALE_LEARNING_SOURCE");
            var rows = new List<LearningArtifactGenerationBatch>();
            foreach (var request in requests)
            {
                var spec = request.Vocabulary?.Batch ?? request.Quiz!.Batch!;
                var row = await uow.Repository<LearningArtifactGenerationBatch>().FirstOrDefaultAsync(x => x.RootJobId == root.Id &&
                    x.Kind == spec.Kind && x.BatchNumber == spec.BatchNumber, cancellationToken: ct);
                if (row is not null)
                {
                    if (row.SourceFingerprint != source || row.ContextFingerprint != contextHash)
                        throw new ConflictException("STALE_LEARNING_SOURCE");
                    rows.Add(row); continue;
                }
                var key = $"learning-{root.Id}-{spec.Kind}-{spec.BatchNumber}";
                var pinned = request with { Vocabulary = request.Vocabulary is null ? null : request.Vocabulary with { RequestId = key },
                    Quiz = request.Quiz is null ? null : request.Quiz with { RequestId = key } };
                row = new() { StoryId = story.Id, StoryVersionId = version.Id, RootJobId = root.Id, RequestedByUserId = actor,
                    SourceFingerprint = source, ContextFingerprint = contextHash, Kind = spec.Kind, BatchNumber = spec.BatchNumber,
                    TargetCount = spec.TargetCount, RequestJson = JsonSerializer.Serialize(pinned, Json) };
                await uow.Repository<LearningArtifactGenerationBatch>().AddAsync(row, ct);
                rows.Add(row);
            }
            root.LeaseExpiresAt = DateTime.UtcNow.AddMinutes(Math.Clamp(options.LeaseMinutes, 8, 30));
            uow.Repository<StoryGenerationJob>().Update(root);
            await uow.SaveChangesAsync(ct);
            await uow.CommitTransactionAsync(ct);
            return rows.Select(x => x.Id).ToArray();
        }
        catch { await uow.RollbackTransactionAsync(CancellationToken.None); uow.DiscardTrackedChanges(); throw; }
    }

    private async Task<IReadOnlyList<LearningArtifactGenerationBatch>> RowsAsync(int root, CancellationToken ct,
        IReadOnlyCollection<string>? kinds = null)
    {
        var all = await uow.Repository<LearningArtifactGenerationBatch>().FindAsync(x => x.RootJobId == root, cancellationToken: ct);
        var rows = kinds is null ? all : all.Where(x => kinds.Contains(x.Kind)).ToArray();
        if (rows.Any(x => x.Status != GenerationJobStatus.Completed || x.ResultJson == null)) throw new ConflictException("LEARNING_BATCH_INCOMPLETE");
        return rows;
    }
    public async Task<GenerateQuizResponse> ReadQuizAsync(int storyId, int versionId, CancellationToken ct)
    {
        var version = await uow.Repository<StoryVersion>().FirstOrDefaultAsync(x => x.Id == versionId && x.IsCurrent, cancellationToken: ct) ?? throw new ConflictException("STALE_LEARNING_SOURCE");
        var story = await uow.Repository<Story>().FirstOrDefaultAsync(x => x.Id == storyId, cancellationToken: ct) ?? throw new ConflictException("STALE_LEARNING_SOURCE");
        var root = await uow.Repository<StoryGenerationJob>().FirstOrDefaultAsync(x => x.StoryId == storyId && x.StoryVersionId == versionId &&
            x.Operation == GenerationJobOperation.GenerateVocabulary && x.Status == GenerationJobStatus.Completed, cancellationToken: ct) ?? throw new ConflictException("LEARNING_BATCH_INCOMPLETE");
        var rows = await RowsAsync(root.Id, ct);
        if (rows.Any(x => x.SourceFingerprint != LearningBatchAccess.Source(story, version))) throw new ConflictException("STALE_LEARNING_SOURCE");
        return Merge(rows).Quiz;
    }
    public async Task ValidateSourceAsync(int rootJobId, Story story, StoryVersion version, int actor, CancellationToken ct)
    {
        var contextHash = await LearningBatchAccess.AuthorizeAsync(uow, guard, story, version, actor, ct);
        var rows = await uow.Repository<LearningArtifactGenerationBatch>().FindAsync(x => x.RootJobId == rootJobId, cancellationToken: ct);
        if (rows.Any(x => x.SourceFingerprint != LearningBatchAccess.Source(story, version) || x.ContextFingerprint != contextHash))
            throw new ConflictException("STALE_LEARNING_SOURCE");
    }
    private static T Deserialize<T>(string? json) => JsonSerializer.Deserialize<T>(json ?? throw new ConflictException("LEARNING_BATCH_INCOMPLETE"), Json)!;
    private static LearningBatchPackage Merge(IReadOnlyList<LearningArtifactGenerationBatch> rows)
    {
        if (!new[] { "vocabulary", "quiz_facts", "quiz_reasoning", "quiz_vocabulary" }.All(kind => rows.Any(x => x.Kind == kind)))
            throw new ConflictException("LEARNING_BATCH_INCOMPLETE");
        var vocabulary = Deserialize<GenerateVocabularyResponse>(rows.Single(x => x.Kind == "vocabulary").ResultJson);
        var quizzes = rows.Where(x => x.Kind.StartsWith("quiz_", StringComparison.Ordinal)).OrderBy(x => x.Kind)
            .Select(x => Deserialize<GenerateQuizResponse>(x.ResultJson)).ToArray();
        var all = quizzes.SelectMany(x => x.Items).ToArray();
        var unique = all.DistinctBy(x => LearningArtifactGrounding.Key(x.Question)).ToArray();
        return new(vocabulary, new() { Items = unique, Metadata = quizzes[0].Metadata,
            ShortfallReason = unique.Length < rows.Where(x => x.Kind.StartsWith("quiz_", StringComparison.Ordinal)).Sum(x => x.TargetCount)
                ? "SOURCE_OR_DUPLICATE_LIMIT: " + string.Join("; ", quizzes.Select(x => x.ShortfallReason).Where(x => x.Length > 0)) : "" });
    }
}
