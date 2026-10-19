using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Application.Features.CreatorAssistance;
using StoryPlatform.Application.Features.CreatorAssistance.Interfaces;
using StoryPlatform.Application.Features.CreatorAssistance.Services;

namespace StoryPlatform.Application.Features.ParentAuthoring;

public sealed class ParentAssistanceService(IUnitOfWork uow, ICreatorAssistanceAiClient ai,
    CreatorAssistanceOptions options, TimeProvider clock, ISupervisionAccessGuard guard,
    AssistanceCancellationRegistry cancellations, ILogger<ParentAssistanceService> logger)
    : StoryAssistanceService(uow, ai, options, clock, cancellations, logger, new ParentAssistanceAccessPolicy(uow, guard));
