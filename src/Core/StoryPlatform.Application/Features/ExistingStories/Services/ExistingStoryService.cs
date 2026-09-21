using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.AIStoryInput.Guardrails;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Application.Features.ExistingStories.DTOs;
using StoryPlatform.Application.Features.ExistingStories.Helpers;
using StoryPlatform.Application.Features.ExistingStories.Interfaces;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ExistingStories.Services;

/// <summary>
/// Triển khai IExistingStoryService.
///
/// Các nguyên tắc:
///   - StoryVersion cũ KHÔNG BAO GIỜ bị overwrite.
///   - Mọi mutation đều lock theo storyId (AcquireTransactionLockAsync).
///   - Phase 2 bị skip hoàn toàn (SKIP OUTLINE).
///   - Sau khi có stable version -> gọi IStableVersionArtifactHandoffService
///     (KHÔNG gọi trực tiếp ContentGenerationService).
/// </summary>
public sealed class ExistingStoryService : IExistingStoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IUnitOfWork _unitOfWork;
    private readonly ISupervisionAccessGuard _accessGuard;
    private readonly IAIStoryGenerationClient _aiClient;
    private readonly IStableVersionArtifactHandoffService _handoff;
    private readonly IAuditLogWriter _auditLog;
    private readonly IInputGuardrail _fullContentGuardrail;

    public ExistingStoryService(
        IUnitOfWork unitOfWork,
        ISupervisionAccessGuard accessGuard,
        IAIStoryGenerationClient aiClient,
        IStableVersionArtifactHandoffService handoff,
        IAuditLogWriter auditLog,
        IInputGuardrail? fullContentGuardrail = null)
    {
        _unitOfWork = unitOfWork;
        _accessGuard = accessGuard;
        _aiClient = aiClient;
        _handoff = handoff;
        _auditLog = auditLog;
        _fullContentGuardrail = fullContentGuardrail ?? new RuleBasedInputGuardrail();
    }

    #region Import

    public async Task<ImportStoryResponseDto> ImportDocumentAsync(
        int userId, ImportStoryDocumentRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request is null || request.Content == Stream.Null || !request.Content.CanRead)
            throw new BadRequestException("File upload không hợp lệ.");
        if (request.ChildProfileId <= 0)
            throw new BadRequestException("ChildProfileId không hợp lệ.");
        if (request.Content.CanSeek && request.Content.Length > 5_000_000)
            throw new BadRequestException("File upload vượt quá giới hạn 5 MB.");

        var extension = Path.GetExtension(request.FileName).ToLowerInvariant();
        string content;
        string inputMethod;
        if (extension == ".docx")
        {
            content = DocxTextExtractor.Extract(request.Content);
            inputMethod = "docx";
        }
        else if (extension == ".txt")
        {
            using var reader = new StreamReader(
                request.Content, new System.Text.UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096, leaveOpen: true);
            content = await reader.ReadToEndAsync(cancellationToken);
            if (content.Length > 200_000)
                throw new BadRequestException("Nội dung trích xuất từ TXT vượt quá 200000 ký tự.");
            inputMethod = "txt";
        }
        else
        {
            throw new BadRequestException("Chỉ hỗ trợ file .txt hoặc .docx.");
        }

        if (string.IsNullOrWhiteSpace(content))
            throw new BadRequestException("Không trích xuất được nội dung văn bản từ file.");

        return await ImportAsync(userId, new ImportStoryRequestDto
        {
            InputMethod = inputMethod,
            Content = content,
            Title = request.Title,
            ChildProfileId = request.ChildProfileId,
            Language = request.Language,
            IdempotencyKey = request.IdempotencyKey
        }, cancellationToken);
    }

    public async Task<ImportStoryResponseDto> ImportAsync(
        int userId, ImportStoryRequestDto request, CancellationToken cancellationToken = default)
    {
        ValidateImportRequest(request);

        var normalized = StoryContentNormalizer.Normalize(request.Content);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new BadRequestException("Nội dung truyện rỗng sau khi chuẩn hoá.");
        var warnings = new List<string>();
        if (normalized!.Length != request.Content?.Length)
            warnings.Add("Đã chuẩn hoá khoảng trắng/newline trong nội dung.");

        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(request.ChildProfileId, cancellationToken)
                    ?? throw new NotFoundException("ChildProfile", request.ChildProfileId);
        if (child.Status != ChildProfileStatus.Active)
            throw new BadRequestException("ChildProfile chưa ở trạng thái Active.");
        await _accessGuard.EnsurePermissionAsync(child.Id, userId, Permission.GenerateStory, cancellationToken);

        var learning = await _unitOfWork.Repository<LearningProfile>().FirstOrDefaultAsync(
            item => item.ChildProfileId == child.Id, cancellationToken: cancellationToken)
            ?? throw new BadRequestException("Child Profile chưa có Learning Profile hợp lệ.");
        if (learning.ReadingLevel is < 1 or > 5)
            throw new BadRequestException("Reading Level của Child Profile phải nằm trong thang 1-5.");

        var policyContext = await LoadSafetyContextAsync(child, cancellationToken);
        var language = string.IsNullOrWhiteSpace(request.Language)
            ? child.Language.Trim().ToLowerInvariant()
            : request.Language.Trim().ToLowerInvariant();
        if (!string.Equals(language, child.Language.Trim().ToLowerInvariant(), StringComparison.Ordinal))
            throw new BadRequestException("Ngôn ngữ truyện không khớp cấu hình Child Profile.");

        var wordCount = StoryContentNormalizer.CountWords(normalized);
        if (wordCount > policyContext.MaximumLength)
            throw new BadRequestException($"Nội dung vượt Maximum Story Length {policyContext.MaximumLength} từ Safety Policy.");

        // Idempotency: nếu cùng user + child + content hash đã import -> trả về bản gốc.
        var idempotencyKey = !string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? request.IdempotencyKey!
            : ExistingStoryIdempotencyKeys.ForImport(userId, child.Id, normalized);

        var existingRequest = await _unitOfWork.Repository<StoryGenerationRequest>().FirstOrDefaultAsync(
            req => req.SubmittedByUserId == userId && req.IdempotencyKey == idempotencyKey,
            cancellationToken: cancellationToken);
        if (existingRequest is not null)
        {
            var existingStory = await _unitOfWork.Repository<Story>().GetByIdAsync(existingRequest.StoryId, cancellationToken);
            var existingVersion = existingStory is null ? null : (await _unitOfWork.Repository<StoryVersion>().FindAsync(
                v => v.StoryId == existingStory.Id && v.IsCurrent, cancellationToken: cancellationToken))
                .OrderByDescending(v => v.VersionNo).FirstOrDefault();
            if (existingStory is not null && existingVersion is not null)
            {
                return new ImportStoryResponseDto
                {
                    StoryId = existingStory.Id,
                    StoryVersionId = existingVersion.Id,
                    StoryStatus = existingStory.Status.ToString(),
                    EditType = existingVersion.EditType.ToString(),
                    ContentLength = existingVersion.Content?.Length,
                    InputStatus = ToInputStatus(existingRequest.Status),
                    ReasonCode = existingRequest.ReasonCode,
                    FallbackMessage = existingRequest.FallbackMessage,
                    CanProceed = existingRequest.Status == GenerationInputStatus.InputAccepted
                };
            }
        }

        Story story = null!;
        StoryVersion v1 = null!;
        StoryGenerationRequest genRequest = null!;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            story = new Story
            {
                Title = string.IsNullOrWhiteSpace(request.Title) ? "Câu chuyện" : request.Title.Trim(),
                Description = null,
                Content = normalized,
                Genre = null,
                MoralLesson = null,
                AgeBand = MapAgeBand(child.AgeBand),
                Language = language,
                ReadingLevel = learning.ReadingLevel,
                VocabularyLevel = $"level_{learning.ReadingLevel}",
                Source = StorySource.Manual,
                Status = StoryStatus.Draft,
                IsPublished = false,
                AuthorUserId = userId,
                ChildProfileId = child.Id
            };
            await _unitOfWork.Repository<Story>().AddAsync(story, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            v1 = new StoryVersion
            {
                StoryId = story.Id,
                VersionNo = 1,
                EditType = VersionEditType.Initial,
                EditorUserId = userId,
                Title = story.Title,
                Content = normalized,
                Lesson = string.Empty,
                IsCurrent = true,
                OutlineApprovedAt = null
            };
            await _unitOfWork.Repository<StoryVersion>().AddAsync(v1, cancellationToken);

            genRequest = new StoryGenerationRequest
            {
                StoryId = story.Id,
                SubmittedByUserId = userId,
                IdempotencyKey = idempotencyKey,
                InputFingerprint = ShortHash(normalized),
                ContextFingerprint = ShortHash($"{child.Id}:{story.AgeBand}:{story.Language}"),
                ContextSnapshotJson = JsonSerializer.Serialize(new
                {
                    childProfileId = child.Id,
                    ageBand = story.AgeBand,
                    language = story.Language,
                    readingLevel = learning.ReadingLevel,
                    comprehensionGoal = learning.ComprehensionGoal,
                    parentalGateEnabled = policyContext.Policy.ParentalGateEnabled,
                    safetyScoreThreshold = policyContext.Policy.SafetyScoreThreshold,
                    comprehensionThresholdPercent = policyContext.Policy.ComprehensionThresholdPercent,
                    source = "existing_story_import"
                }, JsonOptions),
                AcceptedInputJson = JsonSerializer.Serialize(new { title = story.Title, length = normalized.Length }, JsonOptions),
                Status = GenerationInputStatus.CheckingInput,
                AttemptCount = 0,
                MaxAttempts = 1,
                AttemptStartedAt = DateTime.UtcNow,
                GuardrailDecision = null,
                CanRetry = false
            };
            await _unitOfWork.Repository<StoryGenerationRequest>().AddAsync(genRequest, cancellationToken);

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        InputGuardrailResult guardrail;
        try
        {
            guardrail = await _fullContentGuardrail.CheckAsync(new InputGuardrailRequest(
                normalized, [], null, string.Empty, policyContext.BlockedTerms, policyContext.RestrictedTerms),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            guardrail = new InputGuardrailResult(
                InputGuardrailDecision.Error,
                "FULL_CONTENT_GUARDRAIL_ERROR",
                "Không thể kiểm tra toàn bộ nội dung lúc này. Vui lòng thử lại.",
                true,
                RuleBasedInputGuardrail.Version);
        }

        var inputStatus = guardrail.Decision switch
        {
            InputGuardrailDecision.Allow => GenerationInputStatus.InputAccepted,
            InputGuardrailDecision.Block => GenerationInputStatus.InputBlocked,
            _ => GenerationInputStatus.InputCheckFailed
        };
        genRequest.Status = inputStatus;
        genRequest.GuardrailDecision = guardrail.Decision.ToString();
        genRequest.ReasonCode = guardrail.ReasonCode;
        genRequest.FallbackMessage = guardrail.FallbackMessage;
        genRequest.GuardrailCheckVersion = guardrail.CheckVersion;
        genRequest.GuardrailCheckedAt = DateTime.UtcNow;
        genRequest.AttemptStartedAt = null;
        genRequest.CanRetry = guardrail.CanRetry;
        _unitOfWork.Repository<StoryGenerationRequest>().Update(genRequest);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            userId, "ExistingStory.Import", nameof(Story), story.Id,
            beforeState: null, afterState: new { storyId = story.Id, versionId = v1.Id, words = wordCount },
            cancellationToken: cancellationToken);

        return new ImportStoryResponseDto
        {
            StoryId = story.Id,
            StoryVersionId = v1.Id,
            StoryStatus = story.Status.ToString(),
            EditType = v1.EditType.ToString(),
            ContentLength = normalized.Length,
            InputStatus = ToInputStatus(inputStatus),
            ReasonCode = guardrail.ReasonCode,
            FallbackMessage = guardrail.FallbackMessage,
            CanProceed = inputStatus == GenerationInputStatus.InputAccepted,
            Warnings = warnings
        };
    }

    private static void ValidateImportRequest(ImportStoryRequestDto request)
    {
        if (request is null) throw new BadRequestException("Thiếu body request.");
        if (request.ChildProfileId <= 0)
            throw new BadRequestException("ChildProfileId không hợp lệ.");
        var method = request.InputMethod?.Trim().ToLowerInvariant();
        if (method is not ("paste" or "txt" or "docx"))
            throw new BadRequestException("InputMethod không được hỗ trợ (chỉ 'paste', 'txt' hoặc 'docx').");
        if (string.IsNullOrWhiteSpace(request.Content))
            throw new BadRequestException("Nội dung truyện rỗng.");
        if (request.Content!.Length > 200_000)
            throw new BadRequestException("Nội dung vượt quá giới hạn (200000 ký tự).");
    }

    private static string MapAgeBand(AgeBand band) => band switch
    {
        AgeBand.Age_6_8 => "6-8",
        AgeBand.Age_9_12 => "9-12",
        _ => "6-8"
    };

    private static string ShortHash(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

    private async Task<ExistingStorySafetyContext> LoadSafetyContextAsync(
        ChildProfile child, CancellationToken cancellationToken)
    {
        var policy = await _unitOfWork.Repository<SafetyPolicy>().FirstOrDefaultAsync(
            item => item.ChildProfileId == child.Id, cancellationToken: cancellationToken)
            ?? throw new BadRequestException("Child Profile chưa có Safety Policy.");
        if (!policy.ConsentRecorded || !policy.ConsentRecordedAt.HasValue || policy.ConsentPolicyVersion <= 0)
            throw new BadRequestException("CONSENT_REQUIRED: Child Profile chưa có consent hợp lệ để sử dụng AI.");
        if (policy.SafetyScoreThreshold is < 0m or > 100m)
            throw new BadRequestException("Safety Score Threshold phải nằm trong khoảng 0 đến 100.");
        if (policy.ComprehensionThresholdPercent is < 0m or > 100m || policy.ComprehensionWindowSize <= 0)
            throw new BadRequestException("Cấu hình comprehension của Safety Policy không hợp lệ.");

        var personal = await _unitOfWork.Repository<SafetyPolicyCategory>().FindAsync(
            item => item.SafetyPolicyId == policy.Id,
            includeProperties: "ContentCategory", cancellationToken: cancellationToken);
        OrgSafetyPolicyTemplate? organizationPolicy = null;
        IReadOnlyList<OrgSafetyPolicyCategory> organization = [];
        if (child.Scope == ProfileScope.Organization && child.OrganizationId.HasValue)
        {
            organizationPolicy = await _unitOfWork.Repository<OrgSafetyPolicyTemplate>().FirstOrDefaultAsync(
                item => item.OrganizationId == child.OrganizationId.Value, cancellationToken: cancellationToken);
            if (organizationPolicy is not null)
            {
                organization = await _unitOfWork.Repository<OrgSafetyPolicyCategory>().FindAsync(
                    item => item.OrgSafetyPolicyTemplateId == organizationPolicy.Id,
                    includeProperties: "ContentCategory", cancellationToken: cancellationToken);
            }
        }

        var maximums = new[] { policy.MaxStoryLength, organizationPolicy?.MaxStoryLengthBaseline ?? 0 }
            .Where(value => value > 0).ToArray();
        if (maximums.Length == 0)
            throw new BadRequestException("Safety Policy chưa có Maximum Story Length hợp lệ.");

        var rules = new Dictionary<string, (PolicyRule Rule, string Name)>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in personal
                     .Where(item => item.ContentCategory is { IsActive: true })
                     .Select(item => (item.ContentCategory!.Code, item.ContentCategory.DisplayName, item.Rule))
                     .Concat(organization
                         .Where(item => item.ContentCategory is { IsActive: true })
                         .Select(item => (item.ContentCategory!.Code, item.ContentCategory.DisplayName, item.Rule))))
        {
            if (!rules.TryGetValue(item.Code, out var current) || item.Rule > current.Rule)
                rules[item.Code] = (item.Rule, item.DisplayName);
        }

        static string[] Terms(Dictionary<string, (PolicyRule Rule, string Name)> values, PolicyRule rule) => values
            .Where(item => item.Value.Rule == rule)
            .SelectMany(item => new[] { item.Key, item.Value.Name })
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ExistingStorySafetyContext(
            policy, maximums.Min(), Terms(rules, PolicyRule.Blocked), Terms(rules, PolicyRule.Restricted));
    }

    private static string ToInputStatus(GenerationInputStatus status) => status switch
    {
        GenerationInputStatus.InputAccepted => "input_accepted",
        GenerationInputStatus.InputBlocked => "input_blocked",
        GenerationInputStatus.InputCheckFailed => "input_check_failed",
        GenerationInputStatus.CheckingInput => "checking_input",
        _ => "pending_input"
    };

    private sealed record ExistingStorySafetyContext(
        SafetyPolicy Policy,
        int MaximumLength,
        IReadOnlyList<string> BlockedTerms,
        IReadOnlyList<string> RestrictedTerms);

    #endregion

    #region Adapt

    public async Task<VersionMutationResponseDto> AdaptAsync(
        int userId, AdaptExistingStoryRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new BadRequestException("Thiếu body request.");
        if (request.BaseStoryVersionId <= 0)
            throw new BadRequestException("BaseStoryVersionId không hợp lệ.");

        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(request.StoryId, cancellationToken)
                    ?? throw new NotFoundException("Story", request.StoryId);
        if (story.Source != StorySource.Manual)
            throw new BadRequestException("Story không thuộc nhánh Existing.");
        await _accessGuard.EnsurePermissionAsync(story.ChildProfileId, userId, Permission.GenerateStory, cancellationToken);
        await EnsureImportedContentAcceptedAsync(story.Id, cancellationToken);

        StoryVersion? created = null;
        int? newVersionId = null;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(story.Id, cancellationToken);

            var baseVersion = await _unitOfWork.Repository<StoryVersion>()
                .GetByIdAsync(request.BaseStoryVersionId, cancellationToken)
                ?? throw new NotFoundException("StoryVersion", request.BaseStoryVersionId);
            if (baseVersion.StoryId != story.Id || !baseVersion.IsCurrent)
                throw new ConflictException("STALE_BASE_VERSION");
            if (string.IsNullOrWhiteSpace(baseVersion.Content))
                throw new BadRequestException("Base version chưa có nội dung.");

            // Gọi AI để refine.
            var refineRequest = new RefineStoryContentRequest
            {
                RequestId = $"adapt-{story.Id}-v{baseVersion.VersionNo}-{Guid.NewGuid():N}",
                Story = new StoryPlatform.Contracts.AI.Models.StoryContentDto
                {
                    Title = baseVersion.Title,
                    Lesson = baseVersion.Lesson ?? string.Empty,
                    StorySections = [new StoryPlatform.Contracts.AI.Models.StorySectionDto(1, string.Empty, baseVersion.Content!)]
                },
                Language = story.Language ?? "vi",
                AgeBand = story.AgeBand,
                Reasons = string.IsNullOrWhiteSpace(request.Guideline)
                    ? ["Điều chỉnh cho phù hợp với độ tuổi và trình độ đọc."]
                    : [request.Guideline!]
            };
            var refined = await _aiClient.RefineStoryContentAsync(refineRequest, cancellationToken);
            var newContent = ExtractContent(refined.Story);

            var versions = await _unitOfWork.Repository<StoryVersion>().FindAsync(
                v => v.StoryId == story.Id, cancellationToken: cancellationToken);
            var nextVersionNo = versions.Select(v => v.VersionNo).DefaultIfEmpty().Max() + 1;

            baseVersion.IsCurrent = false;
            _unitOfWork.Repository<StoryVersion>().Update(baseVersion);

            created = new StoryVersion
            {
                StoryId = story.Id,
                VersionNo = nextVersionNo,
                EditType = VersionEditType.AiRefined,
                EditorUserId = userId,
                Title = refined.Story.Title.Trim(),
                Content = newContent,
                Lesson = (refined.Story.Lesson ?? baseVersion.Lesson ?? string.Empty).Trim(),
                IsCurrent = true
            };
            await _unitOfWork.Repository<StoryVersion>().AddAsync(created, cancellationToken);

            // Cập nhật header Story cho phù hợp với current version (không phải overwrite content canonical).
            story.Title = created.Title;
            story.Content = created.Content;
            story.MoralLesson = created.Lesson;
            _unitOfWork.Repository<Story>().Update(story);

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            newVersionId = created.Id;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        await _auditLog.LogAsync(
            userId, "ExistingStory.Adapt", nameof(StoryVersion), newVersionId!.Value,
            beforeState: new { baseVersionId = request.BaseStoryVersionId },
            afterState: new { newVersionId = newVersionId, editType = created!.EditType.ToString() },
            cancellationToken: cancellationToken);

        // Không tự gọi handoff: caller (controller) sẽ gọi evaluate trước rồi mới handoff.
        return new VersionMutationResponseDto
        {
            StoryId = story.Id,
            StoryVersionId = newVersionId.Value,
            StoryStatus = story.Status.ToString(),
            EditType = created!.EditType.ToString()
        };
    }

    private static string ExtractContent(StoryPlatform.Contracts.AI.Models.StoryContentDto story)
    {
        if (story.StorySections.Count == 0) return string.Empty;
        return string.Join("\n\n", story.StorySections.OrderBy(s => s.Order).Select(s =>
            string.IsNullOrWhiteSpace(s.Heading) ? s.Content.Trim() : $"{s.Heading.Trim()}\n{s.Content.Trim()}"));
    }

    #endregion

    #region Manual Edit

    public async Task<VersionMutationResponseDto> UpdateContentAsync(
        int userId, ManualEditRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new BadRequestException("Thiếu body request.");
        if (request.BaseStoryVersionId <= 0)
            throw new BadRequestException("BaseStoryVersionId không hợp lệ.");
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Content))
            throw new BadRequestException("Title và Content là bắt buộc.");
        var normalized = StoryContentNormalizer.Normalize(request.Content)!;

        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(request.StoryId, cancellationToken)
                    ?? throw new NotFoundException("Story", request.StoryId);
        if (story.Source != StorySource.Manual)
            throw new BadRequestException("Story không thuộc nhánh Existing.");
        await _accessGuard.EnsurePermissionAsync(story.ChildProfileId, userId, Permission.GenerateStory, cancellationToken);
        await EnsureImportedContentAcceptedAsync(story.Id, cancellationToken);

        StoryVersion? created = null;
        int newVersionId = 0;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(story.Id, cancellationToken);

            var baseVersion = await _unitOfWork.Repository<StoryVersion>()
                .GetByIdAsync(request.BaseStoryVersionId, cancellationToken)
                ?? throw new NotFoundException("StoryVersion", request.BaseStoryVersionId);
            if (baseVersion.StoryId != story.Id || !baseVersion.IsCurrent)
                throw new ConflictException("STALE_BASE_VERSION");

            var versions = await _unitOfWork.Repository<StoryVersion>().FindAsync(
                v => v.StoryId == story.Id, cancellationToken: cancellationToken);
            var nextVersionNo = versions.Select(v => v.VersionNo).DefaultIfEmpty().Max() + 1;

            baseVersion.IsCurrent = false;
            _unitOfWork.Repository<StoryVersion>().Update(baseVersion);

            created = new StoryVersion
            {
                StoryId = story.Id,
                VersionNo = nextVersionNo,
                EditType = VersionEditType.HumanEdited,
                EditorUserId = userId,
                Title = request.Title.Trim(),
                Content = normalized,
                Lesson = (request.Lesson ?? string.Empty).Trim(),
                IsCurrent = true
            };
            await _unitOfWork.Repository<StoryVersion>().AddAsync(created, cancellationToken);

            story.Title = created.Title;
            story.Content = created.Content;
            story.MoralLesson = created.Lesson;
            _unitOfWork.Repository<Story>().Update(story);

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            newVersionId = created.Id;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        await _auditLog.LogAsync(
            userId, "ExistingStory.ManualEdit", nameof(StoryVersion), newVersionId,
            beforeState: new { baseVersionId = request.BaseStoryVersionId },
            afterState: new { newVersionId, editType = created!.EditType.ToString() },
            cancellationToken: cancellationToken);

        return new VersionMutationResponseDto
        {
            StoryId = story.Id,
            StoryVersionId = newVersionId,
            StoryStatus = story.Status.ToString(),
            EditType = created!.EditType.ToString()
        };
    }

    #endregion

    #region Keep Original

    public async Task<VersionMutationResponseDto> KeepOriginalAsync(
        int userId, KeepOriginalRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new BadRequestException("Thiếu body request.");
        if (string.IsNullOrWhiteSpace(request.OverrideReason))
            throw new BadRequestException("OverrideReason là bắt buộc.");

        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(request.StoryId, cancellationToken)
                    ?? throw new NotFoundException("Story", request.StoryId);
        if (story.Source != StorySource.Manual)
            throw new BadRequestException("Story không thuộc nhánh Existing.");
        await _accessGuard.EnsurePermissionAsync(story.ChildProfileId, userId, Permission.GenerateStory, cancellationToken);
        await EnsureImportedContentAcceptedAsync(story.Id, cancellationToken);

        var version = await _unitOfWork.Repository<StoryVersion>().GetByIdAsync(request.StoryVersionId, cancellationToken)
                      ?? throw new NotFoundException("StoryVersion", request.StoryVersionId);
        if (version.StoryId != story.Id)
            throw new BadRequestException("StoryVersion không thuộc Story.");
        if (!version.IsCurrent)
            throw new BadRequestException("StoryVersion không phải current.");

        // Audit log đầy đủ cho keep-original.
        await _auditLog.LogAsync(
            userId, "ExistingStory.KeepOriginal", nameof(Story), story.Id,
            beforeState: new { versionId = version.Id },
            afterState: new { overrideReason = request.OverrideReason, keptVersionId = version.Id },
            cancellationToken: cancellationToken);

        return new VersionMutationResponseDto
        {
            StoryId = story.Id,
            StoryVersionId = version.Id,
            StoryStatus = story.Status.ToString(),
            EditType = version.EditType.ToString(),
            Decision = "KeepOriginal"
        };
    }

    #endregion

    #region Archive

    public async Task<bool> ArchiveAsync(
        int userId, ArchiveExistingStoryRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new BadRequestException("Thiếu body request.");

        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(request.StoryId, cancellationToken)
                    ?? throw new NotFoundException("Story", request.StoryId);
        if (story.Source != StorySource.Manual)
            throw new BadRequestException("Story không thuộc nhánh Existing.");
        await _accessGuard.EnsurePermissionAsync(story.ChildProfileId, userId, Permission.GenerateStory, cancellationToken);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(story.Id, cancellationToken);

            story.Status = StoryStatus.Archived;
            story.ArchivedReason = ArchivedReason.SafetyConcern;
            _unitOfWork.Repository<Story>().Update(story);

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        await _auditLog.LogAsync(
            userId, "ExistingStory.Archive", nameof(Story), story.Id,
            beforeState: new { status = StoryStatus.Draft.ToString() },
            afterState: new { status = story.Status.ToString(), reason = request.Reason },
            cancellationToken: cancellationToken);

        return true;
    }

    #endregion

    private async Task EnsureImportedContentAcceptedAsync(int storyId, CancellationToken cancellationToken)
    {
        var requests = await _unitOfWork.Repository<StoryGenerationRequest>().FindAsync(
            item => item.StoryId == storyId, cancellationToken: cancellationToken);
        var latest = requests.OrderByDescending(item => item.Id).FirstOrDefault();
        if (latest is not null && latest.Status != GenerationInputStatus.InputAccepted)
            throw new ConflictException("HARD_SAFETY_BLOCKED: nội dung gốc chưa vượt qua Full-content Guardrail.");
    }
}
