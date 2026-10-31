using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Learning.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Learning.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.Learning.Services;

public class LearningProfileService : ILearningProfileService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IChildProfileAccessGuard _accessGuard;

    public LearningProfileService(IUnitOfWork unitOfWork, IChildProfileAccessGuard accessGuard)
    {
        _unitOfWork = unitOfWork;
        _accessGuard = accessGuard;
    }

    public async Task<LearningProfileDto> SetLearningProfileAsync(
        int childProfileId, int currentUserId, SetLearningProfileRequestDto request,
        CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsureOwnerAsync(
            childProfileId, currentUserId, cancellationToken);
        ValidateRequest(request);

        var child = await _unitOfWork.Repository<ChildProfile>()
            .GetByIdAsync(childProfileId, cancellationToken)
            ?? throw new NotFoundException("Hồ sơ trẻ", childProfileId);

        var profileRepo = _unitOfWork.Repository<LearningProfile>();
        var topicRepo = _unitOfWork.Repository<LearningProfileTopic>();
        var profile = await profileRepo.FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId,
            cancellationToken: cancellationToken);

        string? previousConfig = null;
        if (profile == null)
        {
            profile = new LearningProfile
            {
                ChildProfileId = childProfileId,
                ReadingLevel = request.ReadingLevel,
                ComprehensionGoal = request.ComprehensionGoal?.Trim(),
                ReadingMode = request.ReadingMode ?? ReadingModeDefaults.ForAgeBand(child.AgeBand)
            };
            await profileRepo.AddAsync(profile, cancellationToken);
        }
        else
        {
            var existingTopics = await topicRepo.FindAsync(
                value => value.LearningProfileId == profile.Id,
                cancellationToken: cancellationToken);
            previousConfig = SerializeConfig(
                profile.ReadingLevel, profile.ReadingMode, profile.ComprehensionGoal,
                existingTopics.Select(value => (value.Topic, value.Relation)));

            profile.ReadingLevel = request.ReadingLevel;
            profile.ComprehensionGoal = request.ComprehensionGoal?.Trim();
            if (request.ReadingMode.HasValue)
            {
                profile.ReadingMode = request.ReadingMode.Value;
            }

            profile.UpdatedAt = DateTime.UtcNow;
            profileRepo.Update(profile);

            if (existingTopics.Count > 0)
            {
                topicRepo.DeleteRange(existingTopics);
            }
        }

        foreach (var topic in request.Topics)
        {
            await topicRepo.AddAsync(new LearningProfileTopic
            {
                LearningProfile = profile,
                Topic = topic.Topic.Trim(),
                Relation = topic.Relation
            }, cancellationToken);
        }

        var newConfig = SerializeConfig(
            profile.ReadingLevel, profile.ReadingMode, profile.ComprehensionGoal,
            request.Topics.Select(value => (value.Topic.Trim(), value.Relation)));

        if (previousConfig != null
            && child.Status == ChildProfileStatus.Active
            && previousConfig != newConfig)
        {
            await _unitOfWork.Repository<ChildProfileVersionHistory>().AddAsync(
                new ChildProfileVersionHistory
                {
                    ChildProfileId = childProfileId,
                    RecommendationId = null,
                    Source = VersionHistorySource.Manual,
                    PreviousConfig = previousConfig,
                    NewConfig = newConfig,
                    AppliedByUserId = currentUserId,
                    VersionStatus = VersionHistoryStatus.ActiveVersion,
                    AppliedAt = DateTime.UtcNow
                }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new LearningProfileDto
        {
            Id = profile.Id,
            ChildProfileId = profile.ChildProfileId,
            ReadingLevel = profile.ReadingLevel,
            ReadingMode = profile.ReadingMode.ToString(),
            ComprehensionGoal = profile.ComprehensionGoal,
            Topics = request.Topics.Select(value => new LearningProfileTopicDto
            {
                Topic = value.Topic.Trim(),
                Relation = value.Relation.ToString()
            }).ToList()
        };
    }

    public async Task<LearningProfileDto> GetLearningProfileAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsureOwnerAsync(childProfileId, currentUserId, cancellationToken);

        var profile = await _unitOfWork.Repository<LearningProfile>().FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId, cancellationToken: cancellationToken);
        if (profile == null)
        {
            throw new NotFoundException("Learning Profile của hồ sơ trẻ", childProfileId);
        }

        var topics = await _unitOfWork.Repository<LearningProfileTopic>().FindAsync(
            value => value.LearningProfileId == profile.Id, cancellationToken: cancellationToken);

        return new LearningProfileDto
        {
            Id = profile.Id,
            ChildProfileId = profile.ChildProfileId,
            ReadingLevel = profile.ReadingLevel,
            ReadingMode = profile.ReadingMode.ToString(),
            ComprehensionGoal = profile.ComprehensionGoal,
            Topics = topics.Select(t => new LearningProfileTopicDto { Topic = t.Topic, Relation = t.Relation.ToString() }).ToList()
        };
    }

    public async Task DeleteLearningProfileAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsureOwnerAsync(childProfileId, currentUserId, cancellationToken);

        var profileRepo = _unitOfWork.Repository<LearningProfile>();
        var profile = await profileRepo.FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId, cancellationToken: cancellationToken);
        if (profile == null)
        {
            throw new NotFoundException("Learning Profile của hồ sơ trẻ", childProfileId);
        }

        var topicRepo = _unitOfWork.Repository<LearningProfileTopic>();
        var topics = await topicRepo.FindAsync(
            value => value.LearningProfileId == profile.Id, cancellationToken: cancellationToken);
        if (topics.Count > 0)
        {
            topicRepo.DeleteRange(topics);
        }

        profileRepo.Delete(profile);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateRequest(SetLearningProfileRequestDto request)
    {
        if (request.ReadingLevel is < 1 or > 5)
        {
            throw new BadRequestException("Reading level phải từ 1 đến 5.");
        }

        if (request.ComprehensionGoal?.Trim().Length > 500)
        {
            throw new BadRequestException("Mục tiêu đọc hiểu tối đa 500 ký tự.");
        }

        if (request.ReadingMode.HasValue && !Enum.IsDefined(request.ReadingMode.Value))
        {
            throw new BadRequestException("Chế độ đọc không hợp lệ.");
        }

        request.Topics ??= new List<SetLearningProfileTopicRequestDto>();
        foreach (var topic in request.Topics)
        {
            if (string.IsNullOrWhiteSpace(topic.Topic) || topic.Topic.Trim().Length > 150)
            {
                throw new BadRequestException("Tên chủ đề phải từ 1 đến 150 ký tự.");
            }

            if (!Enum.IsDefined(topic.Relation))
            {
                throw new BadRequestException("Quan hệ chủ đề không hợp lệ.");
            }
        }
    }

    private static string SerializeConfig(
        int readingLevel, ReadingMode readingMode, string? comprehensionGoal,
        IEnumerable<(string Topic, TopicRelation Relation)> topics) =>
        JsonSerializer.Serialize(new
        {
            readingLevel,
            readingMode = readingMode.ToString(),
            comprehensionGoal,
            topics = topics
                .OrderBy(value => value.Relation)
                .ThenBy(value => value.Topic, StringComparer.Ordinal)
                .Select(value => new { topic = value.Topic, relation = value.Relation.ToString() })
                .ToList()
        });
}
