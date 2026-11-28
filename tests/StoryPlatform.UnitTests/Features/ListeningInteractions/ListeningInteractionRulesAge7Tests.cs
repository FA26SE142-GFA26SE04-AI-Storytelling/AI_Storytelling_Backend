using StoryPlatform.Application.Features.ListeningInteractions.Models;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Domain.Enums;
using Xunit;
using S = StoryPlatform.UnitTests.Features.ListeningInteractions.ListeningTestStory;
using T = StoryPlatform.Domain.Enums.ListeningInteractionType;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

public sealed class ListeningInteractionRulesAge7Tests
{
    private static ListeningRuleContext Ctx(T type) => S.Context(type, 1) with { TargetAge = 7, Zones = [] };

    // Bản nháp hợp lệ lấy từ phản hồi AI giả của tuổi 7; mỗi test chỉ sửa một chỗ.
    private static ListeningInteractionDraft Base(T type) =>
        ListeningInteractionAiMapper.ToDrafts(S.Age7AiResponse(type), type).Single() with { TargetAge = 7 };

    private static string[] Codes(ListeningInteractionDraft item, ListeningRuleContext ctx)
    {
        var mapped = ListeningInteractionMapper.Resolve([item], S.Content, S.Blocks);
        return mapped.Errors.Concat(ListeningInteractionRules.Validate(mapped.Items, ctx)).Select(e => e.Code).Distinct().ToArray();
    }

    // ---- Tìm chi tiết trong tranh ----
    [Fact] public void PictureSearch_Valid() => Assert.Empty(Codes(Base(T.PictureSearch), Ctx(T.PictureSearch)));

    [Fact]
    public void PictureSearch_LabelNotInStory_Rejected() =>
        Assert.Contains("TARGET_NOT_FOUND", Codes(Base(T.PictureSearch) with
            { TargetLabel = "con rồng lửa", Prompt = "Tìm con rồng lửa trong tranh" }, Ctx(T.PictureSearch)));

    [Fact]
    public void PictureSearch_PromptMustNameTheTarget() =>
        Assert.Contains("SCHEMA_INVALID", Codes(Base(T.PictureSearch) with { Prompt = "Tìm một thứ gì đó" }, Ctx(T.PictureSearch)));

    [Fact]
    public void PictureSearch_PromptMustStartWithTim() =>
        Assert.Contains("SCHEMA_INVALID", Codes(Base(T.PictureSearch) with { Prompt = "Thỏ con ở đâu trong tranh" }, Ctx(T.PictureSearch)));

    [Fact]
    public void PictureSearch_LabelOverSixWords_Rejected() =>
        Assert.Contains("SCHEMA_INVALID", Codes(Base(T.PictureSearch) with
            { TargetLabel = "Thỏ con đi lạc vào rừng tối nay", Prompt = "Tìm Thỏ con đi lạc vào rừng tối nay" }, Ctx(T.PictureSearch)));

    [Fact]
    public void PictureSearch_BlankLabel_Rejected() =>
        Assert.Contains("SCHEMA_INVALID", Codes(Base(T.PictureSearch) with { TargetLabel = " " }, Ctx(T.PictureSearch)));

    [Fact]
    public void PictureSearch_WithAnswerCards_Rejected() =>
        Assert.Contains("SCHEMA_INVALID", Codes(Base(T.PictureSearch) with
            { Options = [new ListeningOptionDraft { Text = "x" }] }, Ctx(T.PictureSearch)));

    [Fact]
    public void PictureSearch_QuoteMustNameTheTarget() =>
        Assert.Contains("SCHEMA_INVALID", Codes(Base(T.PictureSearch) with
            { TargetLabel = "bụi cây", Prompt = "Tìm bụi cây trong tranh" }, Ctx(T.PictureSearch)));

    [Fact]
    public void PictureSearch_GradingWordInFeedback_Rejected() =>
        Assert.Contains("GRADING_WORD", Codes(Base(T.PictureSearch) with { NoMatchFeedback = "Con tìm sai rồi" }, Ctx(T.PictureSearch)));

    // ---- Phân biệt đúng sai ----
    [Fact] public void TrueFalse_Valid() => Assert.Empty(Codes(Base(T.TrueFalse), Ctx(T.TrueFalse)));

    [Fact]
    public void TrueFalse_BothTrue_Rejected()
    {
        var item = Base(T.TrueFalse);
        Assert.Contains("MATCH_RULE_INVALID", Codes(item with { Options = [item.Options[0], item.Options[1] with { IsMatch = true }] }, Ctx(T.TrueFalse)));
    }

    [Fact]
    public void TrueFalse_BothFalse_Rejected()
    {
        var item = Base(T.TrueFalse);
        Assert.Contains("MATCH_RULE_INVALID", Codes(item with { Options = [item.Options[0] with { IsMatch = false }, item.Options[1]] }, Ctx(T.TrueFalse)));
    }

    [Fact]
    public void TrueFalse_OnlyOneStatement_Rejected() =>
        Assert.Contains("OPTION_COUNT_INVALID", Codes(Base(T.TrueFalse) with { Options = [Base(T.TrueFalse).Options[0]] }, Ctx(T.TrueFalse)));

    [Fact]
    public void TrueFalse_DuplicateStatements_Rejected()
    {
        var item = Base(T.TrueFalse);
        Assert.Contains("OPTION_COUNT_INVALID", Codes(item with { Options = [item.Options[0], item.Options[1] with { Text = item.Options[0].Text }] }, Ctx(T.TrueFalse)));
    }

    [Fact]
    public void TrueFalse_FalseStatementIsPlainNegation_Rejected()
    {
        var item = Base(T.TrueFalse);
        Assert.Contains("TRUEFALSE_TRIVIAL", Codes(item with
            { Options = [item.Options[0], item.Options[1] with { Text = "Thỏ không nghe tiếng sột soạt sau bụi cây" }] }, Ctx(T.TrueFalse)));
    }

    [Fact]
    public void TrueFalse_FalseStatementChangesADetail_IsAccepted()
    {
        var item = Base(T.TrueFalse);
        Assert.DoesNotContain("TRUEFALSE_TRIVIAL", Codes(item with
            { Options = [item.Options[0], item.Options[1] with { Text = "Thỏ nghe tiếng sột soạt sau cái cây to" }] }, Ctx(T.TrueFalse)));
    }

    [Fact]
    public void TrueFalse_StatementNotAboutTheStory_Rejected()
    {
        var item = Base(T.TrueFalse);
        Assert.Contains("NOT_GROUNDED", Codes(item with
            { Options = [item.Options[0], item.Options[1] with { Text = "Xe buýt chạy nhanh qua cầu" }] }, Ctx(T.TrueFalse)));
    }

    [Fact]
    public void TrueFalse_StatementOverTwelveWords_Rejected()
    {
        var item = Base(T.TrueFalse);
        Assert.Contains("LENGTH_LIMIT", Codes(item with { Options = [item.Options[0] with
            { Text = "Thỏ nghe tiếng sột soạt sau bụi cây khi trời bắt đầu tối và rừng rất yên tĩnh" }, item.Options[1]] }, Ctx(T.TrueFalse)));
    }

    [Fact]
    public void TrueFalse_EvidenceLabel_IsOptionalButBounded()
    {
        var item = Base(T.TrueFalse);
        Assert.Empty(Codes(item with { Options = [item.Options[0] with { ImageDescription = "bụi cây" }, item.Options[1]] }, Ctx(T.TrueFalse)));
        Assert.Contains("LENGTH_LIMIT", Codes(item with
            { Options = [item.Options[0] with { ImageDescription = "một bụi cây rất to ở sau lưng chú thỏ con" }, item.Options[1]] }, Ctx(T.TrueFalse)));
    }

    // ---- Làm theo hành động nhân vật ----
    [Fact] public void FollowAction_Valid() => Assert.Empty(Codes(Base(T.FollowAction), Ctx(T.FollowAction)));

    [Fact]
    public void FollowAction_NoTarget_Rejected()
    {
        var item = Base(T.FollowAction);
        Assert.Contains("OPTION_COUNT_INVALID", Codes(item with { Options = item.Options.Where(o => o.Role == ListeningOptionRole.Item).ToArray() }, Ctx(T.FollowAction)));
    }

    [Fact]
    public void FollowAction_OnlyOneItem_Rejected()
    {
        var item = Base(T.FollowAction);
        Assert.Contains("OPTION_COUNT_INVALID", Codes(item with { Options = [item.Options[0], item.Options[^1]] }, Ctx(T.FollowAction)));
    }

    [Fact]
    public void FollowAction_TwoCorrectItems_Rejected()
    {
        var item = Base(T.FollowAction);
        Assert.Contains("MATCH_RULE_INVALID", Codes(item with { Options = [item.Options[0], item.Options[1] with { IsMatch = true }, item.Options[2]] }, Ctx(T.FollowAction)));
    }

    [Fact]
    public void FollowAction_NoCorrectItem_Rejected()
    {
        var item = Base(T.FollowAction);
        Assert.Contains("MATCH_RULE_INVALID", Codes(item with { Options = [item.Options[0] with { IsMatch = false }, item.Options[1], item.Options[2]] }, Ctx(T.FollowAction)));
    }

    [Fact]
    public void FollowAction_TargetMarkedCorrect_Rejected()
    {
        var item = Base(T.FollowAction);
        Assert.Contains("MATCH_RULE_INVALID", Codes(item with { Options = [item.Options[0], item.Options[1], item.Options[2] with { IsMatch = true }] }, Ctx(T.FollowAction)));
    }

    [Fact]
    public void FollowAction_ItemWithoutPicture_Rejected()
    {
        var item = Base(T.FollowAction);
        Assert.Contains("SCHEMA_INVALID", Codes(item with { Options = [item.Options[0] with { ImageDescription = null }, item.Options[1], item.Options[2]] }, Ctx(T.FollowAction)));
    }

    [Fact]
    public void FollowAction_UnknownCharacter_Rejected() =>
        Assert.Contains("NOT_GROUNDED", Codes(Base(T.FollowAction) with { CharacterName = "Rồng" }, Ctx(T.FollowAction)));

    [Fact]
    public void FollowAction_PromptMustStartWithBeGiup() =>
        Assert.Contains("SCHEMA_INVALID", Codes(Base(T.FollowAction) with { Prompt = "Dắt bác Gấu về hang" }, Ctx(T.FollowAction)));

    [Fact]
    public void FollowAction_OptionWithoutRole_Rejected()
    {
        var item = Base(T.FollowAction);
        Assert.Contains("OPTION_COUNT_INVALID", Codes(item with { Options = [item.Options[0], item.Options[1], item.Options[2] with { Role = ListeningOptionRole.None }] }, Ctx(T.FollowAction)));
    }

    // ---- Ghép từ thành câu ----
    [Fact] public void SentenceBuilder_Valid() => Assert.Empty(Codes(Base(T.SentenceBuilder), Ctx(T.SentenceBuilder)));

    [Fact]
    public void SentenceBuilder_AlternativeOrdersAccepted() =>
        Assert.Empty(Codes(Base(T.SentenceBuilder) with { AcceptedOrdersJson = "[[1,2,3,4],[2,1,3,4]]" }, Ctx(T.SentenceBuilder)));

    [Fact]
    public void SentenceBuilder_FirstOrderMustBeStoryOrder() =>
        Assert.Contains("SENTENCE_ORDERS_INVALID", Codes(Base(T.SentenceBuilder) with { AcceptedOrdersJson = "[[2,1,3,4]]" }, Ctx(T.SentenceBuilder)));

    [Theory]
    [InlineData("[[1,2,3,3]]")]
    [InlineData("[[1,2,3]]")]
    [InlineData("[[1,2,3,4],[1,2,3,4]]")]
    [InlineData("[]")]
    [InlineData("nope")]
    public void SentenceBuilder_BadOrders_Rejected(string json) =>
        Assert.Contains("SENTENCE_ORDERS_INVALID", Codes(Base(T.SentenceBuilder) with { AcceptedOrdersJson = json }, Ctx(T.SentenceBuilder)));

    [Fact]
    public void SentenceBuilder_TooFewCards_Rejected()
    {
        var item = Base(T.SentenceBuilder);
        Assert.Contains("OPTION_COUNT_INVALID", Codes(item with { Options = item.Options.Take(2).ToArray(), AcceptedOrdersJson = "[[1,2]]" }, Ctx(T.SentenceBuilder)));
    }

    [Fact]
    public void SentenceBuilder_CardsMustSpellTheQuote()
    {
        var item = Base(T.SentenceBuilder);
        Assert.Contains("SCHEMA_INVALID", Codes(item with { Options = [item.Options[0], item.Options[1], item.Options[2], item.Options[3] with { Text = "ngôi nhà" }] }, Ctx(T.SentenceBuilder)));
    }

    [Fact]
    public void SentenceBuilder_CardOrderMustBeSequential()
    {
        var item = Base(T.SentenceBuilder);
        Assert.Contains("OPTION_COUNT_INVALID", Codes(item with { Options = [item.Options[0], item.Options[1], item.Options[2], item.Options[3] with { CorrectOrder = 9 }] }, Ctx(T.SentenceBuilder)));
    }

    [Fact]
    public void SentenceBuilder_DuplicateCards_Rejected()
    {
        var item = Base(T.SentenceBuilder);
        Assert.Contains("OPTION_COUNT_INVALID", Codes(item with { Options = [item.Options[0], item.Options[1], item.Options[2], item.Options[3] with { Text = item.Options[2].Text }] }, Ctx(T.SentenceBuilder)));
    }

    [Fact]
    public void SentenceBuilder_QuoteNotBeforeAnchor_Rejected() =>
        Assert.NotEmpty(Codes(Base(T.SentenceBuilder) with { AnchorBlockId = "P1", SourceQuote = "Bác Gấu dẫn Thỏ về tới cửa hang." }, Ctx(T.SentenceBuilder)));

    [Fact]
    public void SentenceBuilder_GradingWordInFeedback_Rejected() =>
        Assert.Contains("GRADING_WORD", Codes(Base(T.SentenceBuilder) with { NoMatchFeedback = "Con xếp sai rồi" }, Ctx(T.SentenceBuilder)));

    [Fact]
    public void SentenceBuilder_MissingFeedback_Rejected() =>
        Assert.Contains("FEEDBACK_MISSING", Codes(Base(T.SentenceBuilder) with { MatchFeedback = null }, Ctx(T.SentenceBuilder)));

    // ---- Sắp xếp 3 thẻ tranh (EventOrdering giữa truyện) ----
    [Fact] public void EventOrdering_Age7_ValidMidStory() => Assert.Empty(Codes(Base(T.EventOrdering), Ctx(T.EventOrdering)));

    [Fact]
    public void EventOrdering_Age7_EventAfterAnchor_Rejected() =>
        Assert.Contains("EVENT_AFTER_ANCHOR", Codes(Base(T.EventOrdering) with { AnchorBlockId = "P3" }, Ctx(T.EventOrdering)));

    [Fact]
    public void EventOrdering_Age7_AnchoredAtLastBlock_Rejected() =>
        Assert.Contains("ANCHOR_INVALID", Codes(Base(T.EventOrdering) with { AnchorBlockId = "P6" }, Ctx(T.EventOrdering)));

    [Fact]
    public void EventOrdering_AnchorIsKeptMidStoryForAge7_ButMovedToTheEndForTheLegacySet()
    {
        var age7 = ListeningInteractionMapper.Resolve([Base(T.EventOrdering)], S.Content, S.Blocks).Items.Single();
        var legacy = ListeningInteractionMapper.Resolve([Base(T.EventOrdering) with { TargetAge = null }], S.Content, S.Blocks).Items.Single();
        Assert.Equal("P5", age7.AnchorBlockId);
        Assert.Equal("P6", legacy.AnchorBlockId);
    }

    [Theory]
    [InlineData(T.EventOrdering, 7, false)]
    [InlineData(T.EventOrdering, 6, true)]
    [InlineData(T.EventOrdering, null, true)]
    [InlineData(T.ReadAloud, 7, true)]
    [InlineData(T.PictureSearch, 7, false)]
    public void IsEndOfStory_DependsOnAge(T type, int? age, bool expected) =>
        Assert.Equal(expected, ListeningInteractionRules.IsEndOfStory(type, age));
}
