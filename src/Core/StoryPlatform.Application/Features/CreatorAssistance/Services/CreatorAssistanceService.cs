using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.CreatorAssistance.Interfaces;

namespace StoryPlatform.Application.Features.CreatorAssistance.Services;

public sealed class CreatorAssistanceService(IUnitOfWork uow, ICreatorAssistanceAiClient ai,
    CreatorAssistanceOptions options, TimeProvider clock, AssistanceCancellationRegistry? cancellations = null,
    ILogger<CreatorAssistanceService>? logger = null)
    : StoryAssistanceService(uow, ai, options, clock, cancellations, logger), ICreatorAssistanceService;
