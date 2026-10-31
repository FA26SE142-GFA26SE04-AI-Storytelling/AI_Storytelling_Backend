using System.Text.Json;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AIStoryInput.DTOs;
using StoryPlatform.Application.Features.AIStoryInput.Guardrails;
using StoryPlatform.Application.Features.AIStoryInput.Services;
using StoryPlatform.Application.Features.StoryWishes;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed partial class AIStoryInputServiceTests
{
    private static SubmitAIStoryInputRequestDto WishInput(int wishId = 9, string topic = "Tình bạn") => new()
    {
        ChildProfileId = 1, SourceWishId = wishId, IdempotencyKey = StoryWishDecisionPolicy.GenerationKey(wishId),
        Topic = topic, Lesson = "Biết giúp đỡ bạn bè", VocabularyLevel = "level_2", CharacterMode = "ai_suggested",
        SettingMode = "ai_suggested", TargetLength = 500
    };

    private static StoryWish PendingWish() => new() { Id = 9, ChildProfileId = 1, Status = StoryWishStatus.Pending };

    [Fact]
    public async Task WishSubmit_LinksRequestAndStory_AndReplayDoesNotCreateAnotherJob()
    {
        var store = CreateEligibleUnitOfWork();
        var wish = PendingWish();
        store.Seed(wish);
        var service = new AIStoryInputService(store, new RuleBasedInputGuardrail(), CreateTokenQuotaService(store));
        var result = await service.SubmitAsync(1, WishInput());
        var replay = await service.SubmitAsync(1, WishInput());
        Assert.Equal(StoryWishStatus.Accepted, wish.Status);
        Assert.Equal(result.StoryId, wish.GeneratedStoryId);
        Assert.Equal(result.RequestId, wish.GenerationRequestId);
        Assert.Equal(1, wish.ResolvedByUserId);
        Assert.Equal(result.RequestId, replay.RequestId);
        Assert.Equal("input_accepted", result.InputStatus);
        Assert.Single(store.Items<Story>());
        Assert.Single(store.Items<StoryGenerationRequest>());
        Assert.Single(store.Items<StoryGenerationJob>());
        Assert.Empty(store.Items<StoryVersion>());
        Assert.False(Assert.Single(store.Items<Story>()).IsPublished);
    }

    [Theory]
    [InlineData(StoryWishStatus.Accepted)]
    [InlineData(StoryWishStatus.Fulfilled)]
    [InlineData(StoryWishStatus.Declined)]
    [InlineData(StoryWishStatus.Expired)]
    public async Task WishSubmit_AlreadyHandled_DoesNotCreateStory(StoryWishStatus status)
    {
        var store = CreateEligibleUnitOfWork();
        var wish = PendingWish();
        wish.Status = status;
        store.Seed(wish);
        var service = new AIStoryInputService(store, new RuleBasedInputGuardrail(), CreateTokenQuotaService(store));
        await Assert.ThrowsAsync<ConflictException>(() => service.SubmitAsync(1, WishInput()));
        Assert.Empty(store.Items<Story>());
        Assert.Empty(store.Items<StoryGenerationRequest>());
        Assert.Empty(store.Items<StoryGenerationJob>());
    }

    [Fact]
    public async Task WishSubmit_ReplayWithDifferentCreativeInput_IsConflict()
    {
        var store = CreateEligibleUnitOfWork();
        store.Seed(PendingWish());
        var service = new AIStoryInputService(store, new RuleBasedInputGuardrail(), CreateTokenQuotaService(store));
        await service.SubmitAsync(1, WishInput());
        await Assert.ThrowsAsync<ConflictException>(() => service.SubmitAsync(1, WishInput(topic: "Một chủ đề khác")));
        Assert.Single(store.Items<Story>());
        Assert.Single(store.Items<StoryGenerationJob>());
    }

    [Fact]
    public async Task WishSubmit_NonOwnerCannotGenerateAgain()
    {
        var store = CreateEligibleUnitOfWork();
        store.Seed(PendingWish());
        store.Seed(new UserAccount { Id = 2, Role = UserRole.User, Status = AccountStatus.Active });
        var service = new AIStoryInputService(store, new RuleBasedInputGuardrail(), CreateTokenQuotaService(store));
        await service.SubmitAsync(1, WishInput());
        await Assert.ThrowsAsync<ForbiddenException>(() => service.SubmitAsync(2, WishInput()));
        Assert.Single(store.Items<Story>());
        Assert.Equal(1, Assert.Single(store.Items<StoryGenerationRequest>()).SubmittedByUserId);
    }

    [Fact]
    public async Task WishSubmit_ForeignChild_IsRejectedBeforePersistence()
    {
        var store = CreateEligibleUnitOfWork();
        var wish = PendingWish();
        wish.ChildProfileId = 99;
        store.Seed(wish);
        var service = new AIStoryInputService(store, new RuleBasedInputGuardrail(), CreateTokenQuotaService(store));
        await Assert.ThrowsAsync<BadRequestException>(() => service.SubmitAsync(1, WishInput()));
        Assert.Empty(store.Items<Story>());
    }

    [Fact]
    public async Task WishSubmit_GuardrailFailure_RemainsLinkedAndReplayDoesNotSpendAgain()
    {
        var store = CreateEligibleUnitOfWork();
        var wish = PendingWish();
        store.Seed(wish);
        var service = new AIStoryInputService(store, new ThrowingGuardrail(), CreateTokenQuotaService(store));
        var failed = await service.SubmitAsync(1, WishInput());
        var replay = await service.SubmitAsync(1, WishInput());
        Assert.Equal("input_check_failed", failed.InputStatus);
        Assert.True(failed.CanRetry);
        Assert.Equal(failed.RequestId, wish.GenerationRequestId);
        Assert.Equal(failed.RequestId, replay.RequestId);
        Assert.Single(store.Items<StoryGenerationRequest>());
        Assert.Empty(store.Items<StoryGenerationJob>());
    }

    [Fact]
    public void SourceWishId_IsServerOnly_AndCannotBeBoundFromJson()
    {
        var request = JsonSerializer.Deserialize<SubmitAIStoryInputRequestDto>(
            "{\"sourceWishId\":9,\"childProfileId\":1}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Null(request.SourceWishId);
        Assert.DoesNotContain("sourceWishId", JsonSerializer.Serialize(WishInput(), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
}
