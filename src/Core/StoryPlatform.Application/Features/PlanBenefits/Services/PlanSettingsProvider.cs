using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.PlanBenefits.Interfaces;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.PlanBenefits.Services;

public class PlanSettingsProvider(IUnitOfWork unitOfWork) : IPlanSettingsProvider
{
    public async Task<PlanSettings> GetAsync(CancellationToken cancellationToken = default) =>
        await unitOfWork.Repository<PlanSettings>().FirstOrDefaultAsync(
            settings => settings.Id == PlanSettings.SingletonId, cancellationToken: cancellationToken)
        ?? new PlanSettings { Id = PlanSettings.SingletonId };
}
