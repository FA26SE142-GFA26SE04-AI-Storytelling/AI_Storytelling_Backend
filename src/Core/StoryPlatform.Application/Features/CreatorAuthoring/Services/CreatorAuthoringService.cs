using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.CreatorAuthoring.Interfaces;
using StoryPlatform.Application.Features.ManualAuthoring.Services;

namespace StoryPlatform.Application.Features.CreatorAuthoring.Services;

// Retain the existing public service and API contract while sharing the writer with Parent.
public sealed class CreatorAuthoringService(IUnitOfWork uow, CreatorAuthoringOptions options,
    IImportPreviewProtector previews)
    : ManualAuthoringService(uow, options, previews, new CreatorManualAuthoringPolicy(uow)), ICreatorAuthoringService;
