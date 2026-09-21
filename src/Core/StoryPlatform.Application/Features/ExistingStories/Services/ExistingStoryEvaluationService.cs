using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Learning.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Learning.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Safety.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Safety.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Application.Features.ContentGeneration.Quality;
using StoryPlatform.Application.Features.ExistingStories.DTOs;
using StoryPlatform.Application.Features.ExistingStories.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ExistingStories.Services;

/// <summary>
/// Đánh giá StoryVersion theo 2 chiều:
///   1) Hard safety: scan content với BlockedCategoryCodes từ SafetyPolicy.
///      Nếu vi phạm -> Blocked (KHÔNG thể override).
///   2) Profile fit: ReadingLevel vs Vocabulary difficulty vs AgeBand vs Length.
///      Không pass profile fit -> AdaptRecommended.
/// </summary>
public sealed class ExistingStoryEvaluationService : IExistingStoryEvaluationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISupervisionAccessGuard _accessGuard;
    private readonly ISafetyPolicyService _safetyPolicyService;
    private readonly ILearningProfileService _learningProfileService;
    private readonly IExistingStoryEvaluationCache _cache;

    public ExistingStoryEvaluationService(
        IUnitOfWork unitOfWork,
        ISupervisionAccessGuard accessGuard,
        ISafetyPolicyService safetyPolicyService,
        ILearningProfileService learningProfileService,
        IExistingStoryEvaluationCache cache)
    {
        _unitOfWork = unitOfWork;
        _accessGuard = accessGuard;
        _safetyPolicyService = safetyPolicyService;
        _learningProfileService = learningProfileService;
        _cache = cache;
    }

    public async Task<ExistingStoryEvaluationDto> EvaluateAsync(
        int userId, int storyId, int storyVersionId, CancellationToken cancellationToken = default)
    {
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(storyId, cancellationToken)
                    ?? throw new NotFoundException("Story", storyId);
        await _accessGuard.EnsurePermissionAsync(story.ChildProfileId, userId, Permission.GenerateStory, cancellationToken);

        var version = await _unitOfWork.Repository<StoryVersion>().GetByIdAsync(storyVersionId, cancellationToken)
                      ?? throw new NotFoundException("StoryVersion", storyVersionId);
        if (version.StoryId != storyId)
            throw new BadRequestException("StoryVersion không thuộc Story.");
        if (!version.IsCurrent)
            throw new BadRequestException("StoryVersion không phải current.");
        if (string.IsNullOrWhiteSpace(version.Content))
            throw new BadRequestException("StoryVersion chưa có nội dung.");

        SafetyPolicyDto? safety = null;
        try
        {
            safety = await _safetyPolicyService.GetSafetyPolicyAsync(
                story.ChildProfileId, userId, cancellationToken);
        }
        catch (NotFoundException)
        {
            safety = null;
        }
        if (safety is null || !safety.ConsentRecorded || !safety.ConsentRecordedAt.HasValue || safety.ConsentPolicyVersion <= 0)
            throw new BadRequestException("CONSENT_REQUIRED: Child Profile chưa có consent hợp lệ để sử dụng AI.");

        var learning = await TryLoadLearningAsync(story.ChildProfileId, userId, cancellationToken);

        var blockedCategories = await LoadBlockedCategoriesAsync(safety, cancellationToken);
        var hardIssues = ScanHardSafety(version.Content!, blockedCategories).ToList();
        var safetyScore = hardIssues.Count == 0 ? 100m : 0m;
        if (safety.SafetyScoreThreshold.HasValue && safetyScore < safety.SafetyScoreThreshold.Value &&
            hardIssues.All(issue => issue.Code != "SAFETY_SCORE_BELOW_THRESHOLD"))
        {
            hardIssues.Add(new EvaluationIssueDto
            {
                Code = "SAFETY_SCORE_BELOW_THRESHOLD",
                Message = $"Điểm an toàn {safetyScore:F2} thấp hơn ngưỡng {safety.SafetyScoreThreshold.Value:F2}."
            });
        }
        var readability = ReadabilityCalculator.EvaluateForProfile(
            version.Content,
            story.Language,
            learning.ReadingLevel ?? story.ReadingLevel ?? 2,
            safety?.ReadabilityScoreThreshold);
        var profileIssues = EvaluateProfileFit(story, version, safety, learning, readability);

        version.ReadabilityFkgl = readability.Metrics.Fkgl;
        version.ReadabilityFre = readability.Metrics.Fre;
        version.SafetyScore = safetyScore;
        _unitOfWork.Repository<StoryVersion>().Update(version);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var decision = hardIssues.Count > 0
            ? ExistingStoryDecision.Blocked
            : profileIssues.Count > 0
                ? ExistingStoryDecision.AdaptRecommended
                : ExistingStoryDecision.Suitable;

        var result = new ExistingStoryEvaluationDto
        {
            StoryId = storyId,
            StoryVersionId = storyVersionId,
            Decision = decision,
            HardSafetyIssues = hardIssues,
            ProfileFitIssues = profileIssues,
            ReadabilityFkgl = readability.Metrics.Fkgl,
            ReadabilityFre = readability.Metrics.Fre,
            SafetyScore = safetyScore,
            WordCount = CountWords(version.Content!),
            CanKeepOriginal = decision != ExistingStoryDecision.Blocked
        };
        _cache.Set(result);
        return result;
    }

    public async Task<ExistingStoryEvaluationDto> GetLatestAsync(
        int userId, int storyId, int storyVersionId, CancellationToken cancellationToken = default)
    {
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(storyId, cancellationToken)
                    ?? throw new NotFoundException("Story", storyId);
        await _accessGuard.EnsurePermissionAsync(story.ChildProfileId, userId, Permission.GenerateStory, cancellationToken);

        var version = await _unitOfWork.Repository<StoryVersion>().GetByIdAsync(storyVersionId, cancellationToken)
                      ?? throw new NotFoundException("StoryVersion", storyVersionId);
        if (version.StoryId != storyId)
            throw new BadRequestException("StoryVersion không thuộc Story.");
        if (!version.IsCurrent)
            throw new BadRequestException("StoryVersion không phải current.");

        return _cache.Get(storyId, storyVersionId)
               ?? await EvaluateAsync(userId, storyId, storyVersionId, cancellationToken);
    }

    private async Task<LearningProfileSnapshot> TryLoadLearningAsync(
        int childProfileId, int userId, CancellationToken cancellationToken)
    {
        try
        {
            var profile = await _learningProfileService.GetLearningProfileAsync(
                childProfileId, userId, cancellationToken);
            return new LearningProfileSnapshot(profile.ReadingLevel, profile.Topics.Select(t => t.Topic).ToArray());
        }
        catch (NotFoundException)
        {
            return new LearningProfileSnapshot(null, Array.Empty<string>());
        }
    }

    private async Task<IReadOnlyList<ContentCategory>> LoadBlockedCategoriesAsync(
        SafetyPolicyDto? safety, CancellationToken cancellationToken)
    {
        var blockedIds = safety?.Categories?
            .Where(c => string.Equals(c.Rule, PolicyRule.Blocked.ToString(), StringComparison.OrdinalIgnoreCase))
            .Select(c => c.ContentCategoryId)
            .Distinct()
            .ToArray() ?? Array.Empty<int>();
        if (blockedIds.Length == 0) return Array.Empty<ContentCategory>();

        return await _unitOfWork.Repository<ContentCategory>().FindAsync(
            category => blockedIds.Contains(category.Id) && category.IsActive,
            cancellationToken: cancellationToken);
    }

    private static IReadOnlyList<EvaluationIssueDto> ScanHardSafety(
        string content, IReadOnlyList<ContentCategory> blockedCategories)
    {
        var issues = new List<EvaluationIssueDto>();
        foreach (var category in blockedCategories)
        {
            var terms = new[] { category.Code, category.DisplayName }
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .Select(term => term.Trim())
                .Where(term => term.Length >= 3 && term.Any(char.IsLetter))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            if (terms.Any(term => Regex.IsMatch(
                    content,
                    $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(term)}(?![\p{{L}}\p{{N}}_])",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            {
                issues.Add(new EvaluationIssueDto
                {
                    Code = "BLOCKED_CATEGORY_MATCH",
                    Message = $"Nội dung khớp nhóm bị chặn '{category.DisplayName}' ({category.Code})."
                });
            }
        }
        return issues;
    }

    private static IReadOnlyList<EvaluationIssueDto> EvaluateProfileFit(
        Story story,
        StoryVersion version,
        SafetyPolicyDto? safety,
        LearningProfileSnapshot learning,
        ReadabilityProfileResult readability)
    {
        var issues = new List<EvaluationIssueDto>();
        var content = version.Content!;

        var maxLength = safety?.MaxStoryLength ?? 5000;
        var wordCount = CountWords(content);
        if (wordCount > maxLength)
        {
            issues.Add(new EvaluationIssueDto
            {
                Code = "LENGTH_EXCEEDS_POLICY",
                Message = $"Độ dài ({wordCount} từ) vượt giới hạn ({maxLength})."
            });
        }

        // Reading level heuristic: too many long words vs reading level.
        if (learning.ReadingLevel.HasValue)
        {
            var avgWordLength = content.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
                .DefaultIfEmpty(string.Empty)
                .Average(w => (double)w.Length);
            var expectedMaxAvg = learning.ReadingLevel.Value switch
            {
                1 => 3.5,
                2 => 4.5,
                3 => 5.5,
                4 => 6.5,
                5 => 7.5,
                _ => 5.5
            };
            if (avgWordLength > expectedMaxAvg)
            {
                issues.Add(new EvaluationIssueDto
                {
                    Code = "VOCABULARY_LEVEL_HIGH",
                    Message = $"Từ vựng trung bình ({avgWordLength:F1} ký tự) cao hơn ReadingLevel {learning.ReadingLevel}."
                });
            }
        }

        if (!readability.Passed)
        {
            issues.Add(new EvaluationIssueDto
            {
                Code = "READABILITY_PROFILE_MISMATCH",
                Message = $"{readability.Metrics.Algorithm} chưa phù hợp ReadingLevel " +
                          $"{learning.ReadingLevel ?? story.ReadingLevel ?? 2}: " +
                          $"grade={readability.Metrics.Fkgl:F2}, ease={readability.Metrics.Fre:F2}."
            });
        }

        // AgeBand rough check.
        if (story.AgeBand == "6-8" && wordCount > 800)
        {
            issues.Add(new EvaluationIssueDto
            {
                Code = "LENGTH_TOO_LONG_FOR_AGE",
                Message = "Câu chuyện quá dài cho nhóm tuổi 6-8."
            });
        }

        return issues;
    }

    private static int CountWords(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return 0;
        return content.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private sealed record LearningProfileSnapshot(int? ReadingLevel, IReadOnlyList<string> Topics);
}
