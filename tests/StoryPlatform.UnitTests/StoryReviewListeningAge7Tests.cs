using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Application.Features.StoryReview.DTOs;
using StoryPlatform.Application.Features.StoryReview.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.ListeningInteractions;
using Xunit;
using S = StoryPlatform.UnitTests.Features.ListeningInteractions.ListeningTestStory;
using T = StoryPlatform.Domain.Enums.ListeningInteractionType;

namespace StoryPlatform.UnitTests;

public sealed class StoryReviewListeningAge7Tests
{
    private static StoryReviewService Build(StoryReviewServiceTests.FakeUnitOfWork uow) =>
        new(uow, S.AiClient().Object, new InMemoryProposalCache(), new InMemoryReviewCompletionStore());

    private static async Task<StoryReviewServiceTests.FakeUnitOfWork> SeedAsync()
    {
        var uow = S.SeedReviewStory();
        await S.SeedPackageAsync(uow);
        return uow;
    }

    private static ListeningInteraction Age7(StoryReviewServiceTests.FakeUnitOfWork uow, T type) =>
        uow.Items<ListeningInteraction>().Single(x => x.Type == type && x.TargetAge == 7 && !x.IsDeleted);

    private static List<ListeningInteractionOption> OptionsOf(StoryReviewServiceTests.FakeUnitOfWork uow, ListeningInteraction interaction) =>
        uow.Items<ListeningInteractionOption>().Where(x => x.ListeningInteractionId == interaction.Id && !x.IsDeleted)
            .OrderBy(x => x.SortOrder).ToList();

    private static Task<ListeningReviewDto> Update(StoryReviewService service, ListeningInteraction interaction, UpdateListeningInteractionRequestDto input) =>
        service.UpdateListeningInteractionAsync(1, 1, interaction.Id, input with { VersionId = 1 });

    private static async Task<string> Rejected(StoryReviewService service, ListeningInteraction interaction, UpdateListeningInteractionRequestDto input) =>
        (await Assert.ThrowsAsync<BadRequestException>(() => Update(service, interaction, input))).Message;

    private static readonly string Box = new HotspotBox(77, 0.4, 0.5, 0.2, 0.2).ToJson();

    // ---- Gói của người duyệt hiển thị các trường mới ----
    [Fact]
    public async Task Review_ShowsHotspotTargetOrdersAndRoles_ForTheAge7Set()
    {
        var uow = await SeedAsync();
        Age7(uow, T.PictureSearch).HotspotJson = Box;
        var dto = await Build(uow).GetListeningForReviewAsync(1, 1);

        var items = dto.AgeSets.Single(x => x.TargetAge == 7).Items;
        Assert.Equal(["PictureSearch", "TrueFalse", "SentenceBuilder", "FollowAction", "EventOrdering"], items.Select(x => x.Type));
        var search = items.Single(x => x.Type == "PictureSearch");
        Assert.Equal("Thỏ con", search.TargetLabel);
        Assert.True(search.HasValidHotspot);
        Assert.Equal((77, 0.4, 0.5), (search.Hotspot!.AssetId, search.Hotspot.X, search.Hotspot.Y));
        Assert.Equal([[1, 2, 3, 4]], items.Single(x => x.Type == "SentenceBuilder").AcceptedOrders!);
        Assert.Equal([ListeningOptionRole.Item, ListeningOptionRole.Item, ListeningOptionRole.Target],
            items.Single(x => x.Type == "FollowAction").Options.Select(o => o.Role));
        Assert.Empty(dto.Issues);
    }

    [Fact]
    public async Task Review_PictureSearchWithoutBox_ReportsNoValidHotspot()
    {
        var uow = await SeedAsync();
        var dto = await Build(uow).GetListeningForReviewAsync(1, 1);
        var search = dto.AgeSets.Single(x => x.TargetAge == 7).Items.Single(x => x.Type == "PictureSearch");
        Assert.False(search.HasValidHotspot);
        Assert.Null(search.Hotspot);
    }

    // ---- Sửa tên đồ vật / mô tả / câu trích: khung cũ phải bị xóa để định vị lại ----
    [Fact]
    public async Task Edit_TargetLabel_ClearsTheBoxSoItIsLocatedAgain()
    {
        var uow = await SeedAsync();
        var search = Age7(uow, T.PictureSearch);
        search.HotspotJson = Box;
        await Update(Build(uow), search, new() { TargetLabel = "rừng", Prompt = "Tìm rừng trong tranh" });
        Assert.Equal("rừng", search.TargetLabel);
        Assert.Null(search.HotspotJson);
    }

    [Fact]
    public async Task Edit_OnlyFeedback_KeepsTheBox()
    {
        var uow = await SeedAsync();
        var search = Age7(uow, T.PictureSearch);
        search.HotspotJson = Box;
        await Update(Build(uow), search, new() { MatchFeedback = "Con tìm rất nhanh!" });
        Assert.Equal(Box, search.HotspotJson);
    }

    [Fact]
    public async Task Edit_SourceQuote_ToAnotherSentence_ClearsTheBox()
    {
        var uow = S.SeedReviewStory();
        var p2 = S.Block("P2");
        var first = "Thỏ nghe tiếng sột soạt sau bụi cây.";
        var second = "Thỏ hỏi: \"Có ai ở đó không?\"";
        var start = S.Content.IndexOf(first, StringComparison.Ordinal);
        var search = new ListeningInteraction
        {
            Id = 950, StoryVersionId = 1, TargetAge = 7, Type = T.PictureSearch, AnchorOffset = p2.EndOffset, Prompt = "Tìm Thỏ trong tranh",
            TargetLabel = "Thỏ", SourceQuote = first, SourceStart = start, SourceEnd = start + first.Length,
            MatchFeedback = "Con tìm giỏi quá!", NoMatchFeedback = "Nhìn kỹ lại nhé.", HotspotJson = Box
        };
        uow.Seed(search);
        await Update(Build(uow), search, new() { SourceQuote = second });
        Assert.Equal(second, search.SourceQuote);
        Assert.NotEqual(start, search.SourceStart);
        Assert.Null(search.HotspotJson);
    }

    [Fact]
    public async Task Edit_TrueFalseEvidence_ClearsOnlyThatOptionsBox()
    {
        var uow = await SeedAsync();
        var trueFalse = Age7(uow, T.TrueFalse);
        var options = OptionsOf(uow, trueFalse);
        options[0].ImageDescription = "bụi cây";
        options[0].HotspotJson = Box;
        options[1].ImageDescription = "bụi cây";
        options[1].HotspotJson = Box;
        await Update(Build(uow), trueFalse, new() { Options = [new() { Id = options[0].Id, ImageDescription = "bụi cây lớn" }] });
        Assert.Equal("bụi cây lớn", options[0].ImageDescription);
        Assert.Null(options[0].HotspotJson);
        Assert.Equal(Box, options[1].HotspotJson);
    }

    [Fact]
    public async Task Edit_TrueFalseStatementText_KeepsTheEvidenceBox()
    {
        var uow = await SeedAsync();
        var trueFalse = Age7(uow, T.TrueFalse);
        var options = OptionsOf(uow, trueFalse);
        options[0].ImageDescription = "bụi cây";
        options[0].HotspotJson = Box;
        await Update(Build(uow), trueFalse, new() { Options = [new() { Id = options[0].Id, Text = "Thỏ nghe tiếng động sau bụi cây" }] });
        Assert.Equal(Box, options[0].HotspotJson);
    }

    // ---- Luật tuổi 7 khi sửa tay ----
    [Fact]
    public async Task Edit_SentenceBuilder_AlternativeOrdersAreSavedAsJson()
    {
        var uow = await SeedAsync();
        var builder = Age7(uow, T.SentenceBuilder);
        await Update(Build(uow), builder, new() { AcceptedOrders = [[1, 2, 3, 4], [2, 1, 3, 4]] });
        Assert.Equal("[[1,2,3,4],[2,1,3,4]]", builder.AcceptedOrdersJson);
    }

    [Theory]
    [InlineData(1, 2, 3, 3)]
    [InlineData(2, 1, 3, 4)]       // thứ tự đầu không phải thứ tự trong truyện
    public async Task Edit_SentenceBuilder_BadOrders_AreRejected_AndNothingSaved(params int[] order)
    {
        var uow = await SeedAsync();
        var builder = Age7(uow, T.SentenceBuilder);
        var before = builder.AcceptedOrdersJson;
        var message = await Rejected(Build(uow), builder, new() { AcceptedOrders = [order] });
        Assert.Contains("SENTENCE_ORDERS_INVALID@acceptedOrders", message);
        Assert.Equal(before, builder.AcceptedOrdersJson);
    }

    [Fact]
    public async Task Edit_PictureSearch_TargetNotInStory_IsRejected()
    {
        var uow = await SeedAsync();
        var search = Age7(uow, T.PictureSearch);
        var message = await Rejected(Build(uow), search, new() { TargetLabel = "con rồng lửa", Prompt = "Tìm con rồng lửa" });
        Assert.Contains("TARGET_NOT_FOUND@targetLabel", message);
        Assert.Equal("Thỏ con", search.TargetLabel);
    }

    [Fact]
    public async Task Edit_TrueFalse_BothTrue_IsRejected()
    {
        var uow = await SeedAsync();
        var trueFalse = Age7(uow, T.TrueFalse);
        var options = OptionsOf(uow, trueFalse);
        var message = await Rejected(Build(uow), trueFalse, new() { Options = options.Where(o => !o.IsMatch).Select(o => new UpdateListeningOptionDto { Id = o.Id, IsMatch = true }).ToArray() });
        Assert.Contains("MATCH_RULE_INVALID@options", message);
        Assert.Single(OptionsOf(uow, trueFalse), o => o.IsMatch);
    }

    [Fact]
    public async Task Edit_TrueFalse_PlainNegationAsFalseStatement_IsRejected()
    {
        var uow = await SeedAsync();
        var trueFalse = Age7(uow, T.TrueFalse);
        var options = OptionsOf(uow, trueFalse);
        var truth = options.Single(o => o.IsMatch);
        var lie = options.Single(o => !o.IsMatch);
        var message = await Rejected(Build(uow), trueFalse, new() { Options = [new() { Id = lie.Id, Text = truth.Text.Replace("Thỏ nghe", "Thỏ không nghe") }] });
        Assert.Contains("TRUEFALSE_TRIVIAL@options", message);
    }

    [Fact]
    public async Task Edit_FollowAction_ItemWithoutPicture_IsRejected()
    {
        var uow = await SeedAsync();
        var action = Age7(uow, T.FollowAction);
        var item = OptionsOf(uow, action).First(o => o.Role == ListeningOptionRole.Item);
        // Người duyệt không được xóa mô tả tranh: chuỗi rỗng bị chặn trước khi chạy luật.
        var message = await Rejected(Build(uow), action, new() { Options = [new() { Id = item.Id, ImageDescription = "  " }] });
        Assert.Contains("FIELD_EMPTY", message);
        Assert.False(string.IsNullOrWhiteSpace(item.ImageDescription));
    }

    [Fact]
    public async Task Edit_FollowAction_RemovingTheTargetRole_IsRejected()
    {
        var uow = await SeedAsync();
        var action = Age7(uow, T.FollowAction);
        var target = OptionsOf(uow, action).Single(o => o.Role == ListeningOptionRole.Target);
        var message = await Rejected(Build(uow), action, new() { Options = [new() { Id = target.Id, Role = ListeningOptionRole.Item }] });
        Assert.Contains("OPTION_COUNT_INVALID@options", message);
        Assert.Equal(ListeningOptionRole.Target, target.Role);
    }

    [Fact]
    public async Task Edit_FollowAction_SecondCorrectItem_IsRejected()
    {
        var uow = await SeedAsync();
        var action = Age7(uow, T.FollowAction);
        var decoy = OptionsOf(uow, action).First(o => o.Role == ListeningOptionRole.Item && !o.IsMatch);
        var message = await Rejected(Build(uow), action, new() { Options = [new() { Id = decoy.Id, IsMatch = true }] });
        Assert.Contains("MATCH_RULE_INVALID@options", message);
    }

    [Fact]
    public async Task Edit_Age7Interaction_IsValidatedWithAge7Rules_NotTheLegacyOnes()
    {
        var uow = await SeedAsync();
        var ordering = Age7(uow, T.EventOrdering);
        // Neo giữa truyện chỉ hợp lệ ở tuổi 7: sửa chữ phản hồi không được đẩy nó về cuối truyện.
        var anchor = ordering.AnchorOffset;
        await Update(Build(uow), ordering, new() { MatchFeedback = "Con xếp rất giỏi!" });
        Assert.Equal(anchor, ordering.AnchorOffset);
        Assert.NotEqual(S.Blocks[^1].EndOffset, ordering.AnchorOffset);
    }

    [Fact]
    public async Task Edit_UntaggedRow_IsNotFound()
    {
        var uow = await SeedAsync();
        uow.Seed(new ListeningInteraction { Id = 951, StoryVersionId = 1, Type = T.PictureSearch, TargetLabel = "Thỏ con", Prompt = "Tìm Thỏ con",
            AnchorOffset = S.Block("P1").EndOffset });
        await Assert.ThrowsAsync<NotFoundException>(() => Update(Build(uow), uow.Items<ListeningInteraction>().Single(x => x.Id == 951), new() { MatchFeedback = "Hay!" }));
    }

    // ---- Duyệt xong khi đủ cả hai bộ ----
    [Fact]
    public async Task Complete_NeedsBothBuiltAgeSets()
    {
        var uow = S.SeedReviewStory();
        await S.SeedPackageAsync(uow, T.Checkpoint, T.RolePlay, T.CharacterLetter, T.PicturePrediction, T.WhoSaidIt);   // chỉ bộ tuổi 6
        var service = Build(uow);
        await Assert.ThrowsAsync<ConflictException>(() => service.CompleteListeningReviewAsync(1, 1));

        var full = await SeedAsync();
        Assert.True(await Build(full).CompleteListeningReviewAsync(1, 1));
    }
}
