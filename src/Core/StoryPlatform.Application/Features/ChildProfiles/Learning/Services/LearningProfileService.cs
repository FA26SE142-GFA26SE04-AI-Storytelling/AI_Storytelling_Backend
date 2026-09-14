using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Learning.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Learning.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.ChildProfiles.Learning.Services;

public class LearningProfileService : ILearningProfileService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISupervisionAccessGuard _accessGuard;

    public LearningProfileService(IUnitOfWork unitOfWork, ISupervisionAccessGuard accessGuard)
    {
        _unitOfWork = unitOfWork;
        _accessGuard = accessGuard;
    }

    public async Task<LearningProfileDto> SetLearningProfileAsync(
        int childProfileId, int currentUserId, SetLearningProfileRequestDto request,
        CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsureActiveSupervisionAsync(
            childProfileId, currentUserId, cancellationToken);
        ValidateRequest(request);

        var profileRepo = _unitOfWork.Repository<LearningProfile>();
        var profile = await profileRepo.FirstOrDefaultAsync(
            value => value.ChildProfileId == childProfileId,
            cancellationToken: cancellationToken);

        if (profile == null)
        {
            profile = new LearningProfile
            {
                ChildProfileId = childProfileId,
                ReadingLevel = request.ReadingLevel,
                ComprehensionGoal = request.ComprehensionGoal?.Trim()
            };
            await profileRepo.AddAsync(profile, cancellationToken);
        }
        else
        {
            profile.ReadingLevel = request.ReadingLevel;
            profile.ComprehensionGoal = request.ComprehensionGoal?.Trim();
            profile.UpdatedAt = DateTime.UtcNow;
            profileRepo.Update(profile);

            var existingTopics = await _unitOfWork.Repository<LearningProfileTopic>()
                .FindAsync(
                    value => value.LearningProfileId == profile.Id,
                    cancellationToken: cancellationToken);
            if (existingTopics.Count > 0)
            {
                _unitOfWork.Repository<LearningProfileTopic>().DeleteRange(existingTopics);
            }
        }

        var topicRepo = _unitOfWork.Repository<LearningProfileTopic>();
        foreach (var topic in request.Topics)
        {
            await topicRepo.AddAsync(new LearningProfileTopic
            {
                LearningProfile = profile,
                Topic = topic.Topic.Trim(),
                Relation = topic.Relation
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new LearningProfileDto
        {
            Id = profile.Id,
            ChildProfileId = profile.ChildProfileId,
            ReadingLevel = profile.ReadingLevel,
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
        await _accessGuard.EnsureActiveSupervisionAsync(childProfileId, currentUserId, cancellationToken);

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
            ComprehensionGoal = profile.ComprehensionGoal,
            Topics = topics.Select(t => new LearningProfileTopicDto { Topic = t.Topic, Relation = t.Relation.ToString() }).ToList()
        };
    }

    public async Task DeleteLearningProfileAsync(
        int childProfileId, int currentUserId, CancellationToken cancellationToken = default)
    {
        await _accessGuard.EnsureActiveSupervisionAsync(childProfileId, currentUserId, cancellationToken);

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
}
