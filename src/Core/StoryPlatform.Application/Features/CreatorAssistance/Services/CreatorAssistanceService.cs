using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.CreatorAssistance.Interfaces;
using StoryPlatform.Application.Features.PlanBenefits.Interfaces;

namespace StoryPlatform.Application.Features.CreatorAssistance.Services;

public sealed class CreatorAssistanceService(IUnitOfWork uow, ICreatorAssistanceAiClient ai,
    CreatorAssistanceOptions options, TimeProvider clock, AssistanceCancellationRegistry? cancellations = null,
    ILogger<CreatorAssistanceService>? logger = null, IPlanBenefitService? planBenefits = null, IPlanUsageService? planUsage = null)
    : StoryAssistanceService(uow, ai, options, clock, cancellations, logger, planBenefits: planBenefits, planUsage: planUsage), ICreatorAssistanceService;
