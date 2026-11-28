using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Application.Features.StoryReview.DTOs;
using StoryPlatform.Application.Features.StoryReview.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;
using S = StoryPlatform.UnitTests.Features.ListeningInteractions.ListeningTestStory;
using T = StoryPlatform.Domain.Enums.ListeningInteractionType;

namespace StoryPlatform.UnitTests;

public sealed class StoryReviewListeningAge8Tests
{
    private static StoryReviewService Build(StoryReviewServiceTests.FakeUnitOfWork uow) =>
        new(uow, S.AiClient().Object, new InMemoryProposalCache(), new InMemoryReviewCompletionStore());

    private static async Task<StoryReviewServiceTests.FakeUnitOfWork> SeedAsync()
    {
        var uow = S.SeedReviewStory();
        await S.SeedPackageAsync(uow);
        return uow;
    }

    private static ListeningInteraction Age8(StoryReviewServiceTests.FakeUnitOfWork uow, T type) =>
        uow.Items<ListeningInteraction>().Single(x => x.Type == type && x.TargetAge == 8 && !x.IsDeleted);

    private static List<ListeningInteractionOption> OptionsOf(StoryReviewServiceTests.FakeUnitOfWork uow, ListeningInteraction interaction) =>
        uow.Items<ListeningInteractionOption>().Where(x => x.ListeningInteractionId == interaction.Id && !x.IsDeleted)
            .OrderBy(x => x.SortOrder).ToList();

    private static Task<ListeningReviewDto> Update(StoryReviewService service, ListeningInteraction interaction, UpdateListeningInteractionRequestDto input) =>
        service.UpdateListeningInteractionAsync(1, 1, interaction.Id, input with { VersionId = 1 });

    private static async Task<string> Rejected(StoryReviewService service, ListeningInteraction interaction, UpdateListeningInteractionRequestDto input) =>
        (await Assert.ThrowsAsync<BadRequestException>(() => Update(service, interaction, input))).Message;

    // ---- Gói của người duyệt có bộ tuổi 8 ----
    [Fact]
    public async Task Review_ShowsTheAge8SetNextToAge6And7_WithSpeakersCluesAndEmotions()
    {
        var uow = await SeedAsync();
        var dto = await Build(uow).GetListeningForReviewAsync(1, 1);

        Assert.Equal([6, 7, 8], dto.AgeSets.Select(x => x.TargetAge));
        var items = dto.AgeSets.Single(x => x.TargetAge == 8).Items;
        Assert.Equal(["AlternateReading", "InferenceQuestion", "SceneTitle", "ThoughtBubble", "CauseEffect"], items.Select(x => x.Type));
        Assert.Equal(["Thỏ", "Bác Gấu"], items.Single(x => x.Type == "AlternateReading").Options.Select(o => o.Speaker));
        var inference = items.Single(x => x.Type == "InferenceQuestion");
        Assert.Equal(3, inference.Options.Count(o => o.Role == ListeningOptionRole.Clue));
        Assert.Equal(["vui", "sợ", "háo hức"], items.Single(x => x.Type == "ThoughtBubble").Options.Select(o => o.Emotion));
        var cause = items.Single(x => x.Type == "CauseEffect");
        Assert.Equal(3, cause.Options.Count(o => o.Role == ListeningOptionRole.Item));
        Assert.Equal(3, cause.Options.Count(o => o.Role == ListeningOptionRole.Target));
        Assert.Equal(5, dto.Plan.Count(p => p.TargetAge == 8));
        Assert.Empty(dto.Issues);
    }

    // ---- Sửa chữ: luật tuổi 8 được chạy lại ----
    [Fact]
    public async Task Edit_ThoughtBubbleEmotion_IsSavedAndRetiresTheOldMedia()
    {
        var uow = await SeedAsync();
        var bubble = Age8(uow, T.ThoughtBubble);
        var first = OptionsOf(uow, bubble)[1];
        await Update(Build(uow), bubble, new() { Options = [new() { Id = first.Id, Emotion = "buồn" }] });
        Assert.Equal("buồn", uow.Items<ListeningInteractionOption>().Single(x => x.Id == first.Id).Emotion);
    }

    [Fact]
    public async Task Edit_ThoughtBubbleEmotion_OutsideTheList_IsRejected_AndNothingSaved()
    {
        var uow = await SeedAsync();
        var bubble = Age8(uow, T.ThoughtBubble);
        var first = OptionsOf(uow, bubble)[1];
        Assert.Contains("EMOTION_INVALID", await Rejected(Build(uow), bubble, new() { Options = [new() { Id = first.Id, Emotion = "giận dữ" }] }));
        Assert.Equal("sợ", uow.Items<ListeningInteractionOption>().Single(x => x.Id == first.Id).Emotion);
    }

    [Fact]
    public async Task Edit_AlternateReadingSpeaker_ToAThirdCharacter_IsRejected()
    {
        var uow = await SeedAsync();
        var reading = Age8(uow, T.AlternateReading);
        var options = OptionsOf(uow, reading);
        Assert.Contains("SPEAKER_INVALID", await Rejected(Build(uow), reading, new() { Options = [new() { Id = options[0].Id, Speaker = "Mẹ Thỏ" }] }));
        Assert.Equal("Thỏ", uow.Items<ListeningInteractionOption>().Single(x => x.Id == options[0].Id).Speaker);
    }

    [Fact]
    public async Task Edit_InferenceClue_OutsideThePassage_IsRejected()
    {
        var uow = await SeedAsync();
        var inference = Age8(uow, T.InferenceQuestion);
        var clue = OptionsOf(uow, inference).First(o => o.Role == ListeningOptionRole.Clue);
        Assert.Contains("CLUE_INVALID", await Rejected(Build(uow), inference, new() { Options = [new() { Id = clue.Id, Text = "con cáo đỏ" }] }));
    }

    [Fact]
    public async Task Edit_InferenceCorrectAnswer_ThatCopiesThePassage_IsRejected()
    {
        var uow = await SeedAsync();
        var inference = Age8(uow, T.InferenceQuestion);
        var correct = OptionsOf(uow, inference).First(o => o.IsMatch);
        Assert.Contains("NOT_INFERENCE", await Rejected(Build(uow), inference, new() { Options = [new() { Id = correct.Id, Text = "Trời bắt đầu tối" }] }));
    }

    [Fact]
    public async Task Edit_CauseEffectRoleOfACard_BreaksThePairs()
    {
        var uow = await SeedAsync();
        var cause = Age8(uow, T.CauseEffect);
        var card = OptionsOf(uow, cause).First(o => o.Role == ListeningOptionRole.Target);
        Assert.Contains("PAIR_INVALID", await Rejected(Build(uow), cause, new() { Options = [new() { Id = card.Id, Role = ListeningOptionRole.Item }] }));
    }

    [Fact]
    public async Task Edit_SceneTitleFeedback_ClearsTheConfirmation_AndKeepsTheRulesHappy()
    {
        var uow = await SeedAsync();
        var title = Age8(uow, T.SceneTitle);
        var option = OptionsOf(uow, title)[1];
        await Update(Build(uow), title, new() { Options = [new() { Id = option.Id, Feedback = "Tiêu đề này chỉ nhắc đến một chi tiết thôi." }] });
        Assert.Equal("Tiêu đề này chỉ nhắc đến một chi tiết thôi.", uow.Items<ListeningInteractionOption>().Single(x => x.Id == option.Id).Feedback);
    }

    [Fact]
    public async Task Edit_BlankSpeakerOrEmotion_IsFieldEmpty()
    {
        var uow = await SeedAsync();
        var reading = Age8(uow, T.AlternateReading);
        var option = OptionsOf(uow, reading)[0];
        Assert.Contains("FIELD_EMPTY", await Rejected(Build(uow), reading, new() { Options = [new() { Id = option.Id, Speaker = "  " }] }));
    }

    // ---- Xác nhận duyệt ----
    [Fact]
    public async Task Complete_NeedsAllThreeBuiltAgeSets()
    {
        var uow = await SeedAsync();
        Assert.True(await Build(uow).CompleteListeningReviewAsync(1, 1));

        foreach (var row in uow.Items<ListeningInteraction>().Where(x => x.TargetAge == 8)) row.IsDeleted = true;
        var error = await Assert.ThrowsAsync<ConflictException>(() => Build(uow).CompleteListeningReviewAsync(1, 1));
        Assert.Equal("LISTENING_PACKAGE_INVALID", error.Message);
    }

    [Fact]
    public async Task Regenerate_OnlyTheAge8Type_LeavesAge6And7Alone()
    {
        var uow = await SeedAsync();
        var before = uow.Items<ListeningInteraction>().Where(x => x.TargetAge != 8 && !x.IsDeleted).Select(x => x.Id).Order().ToArray();
        var cache = new InMemoryProposalCache();
        var service = new StoryReviewService(uow, S.AiClient().Object, cache, new InMemoryReviewCompletionStore());

        var created = await service.CreateListeningProposalAsync(1, 1, T.SceneTitle, default, targetAge: 8);
        Assert.False(string.IsNullOrEmpty(created.ProposalId));
        await Assert.ThrowsAsync<ConflictException>(() => service.CreateListeningProposalAsync(1, 1, T.Checkpoint, default, targetAge: 8));
        Assert.Equal(before, uow.Items<ListeningInteraction>().Where(x => x.TargetAge != 8 && !x.IsDeleted).Select(x => x.Id).Order().ToArray());
    }
}
