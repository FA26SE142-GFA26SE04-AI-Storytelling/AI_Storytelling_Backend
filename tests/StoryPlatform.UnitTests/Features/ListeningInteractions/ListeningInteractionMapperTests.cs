using StoryPlatform.Application.Features.ListeningInteractions.Models;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Domain.Enums;
using Xunit;
using S = StoryPlatform.UnitTests.Features.ListeningInteractions.ListeningTestStory;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

public sealed class ListeningInteractionMapperTests
{
    [Fact]
    public void Checkpoint_ResolvesAnchorAndQuote()
    {
        var draft = S.Checkpoint("P2", CheckpointKind.Prediction, "Thỏ nghe tiếng sột soạt sau bụi cây.");
        var result = ListeningInteractionMapper.Resolve([draft], S.Content, S.Blocks);
        Assert.Empty(result.Errors);
        var item = result.Items[0];
        Assert.Equal(S.Block("P2").EndOffset, item.AnchorOffset);
        Assert.Equal(S.Content.IndexOf("Thỏ nghe", StringComparison.Ordinal), item.SourceStart);
        Assert.Equal("Thỏ nghe tiếng sột soạt sau bụi cây.", S.Content[item.SourceStart!.Value..item.SourceEnd!.Value]);
    }

    [Fact]
    public void Checkpoint_QuoteAfterAnchor_IsRejected()
    {
        var draft = S.Checkpoint("P2", CheckpointKind.Prediction, "Hóa ra đó là bác Gấu đến giúp Thỏ tìm đường về.");
        var result = ListeningInteractionMapper.Resolve([draft], S.Content, S.Blocks);
        Assert.Equal([new ListeningValidationError("QUOTE_AFTER_ANCHOR", 0, "sourceQuote")], result.Errors);
    }

    [Fact]
    public void UnknownQuoteAndAnchor_AreRejected()
    {
        var badQuote = S.Checkpoint("P2", CheckpointKind.Prediction, "Câu không có trong truyện.");
        var badAnchor = S.Checkpoint("P9", CheckpointKind.Prediction, "Thỏ con đi lạc vào rừng.");
        var result = ListeningInteractionMapper.Resolve([badQuote, badAnchor], S.Content, S.Blocks);
        Assert.Contains(new ListeningValidationError("QUOTE_NOT_FOUND", 0, "sourceQuote"), result.Errors);
        Assert.Contains(new ListeningValidationError("ANCHOR_INVALID", 1, "afterBlockId"), result.Errors);
    }

    [Fact]
    public void RolePlay_QuoteMustBeInAnchorBlock()
    {
        var ok = new ListeningInteractionDraft { Type = ListeningInteractionType.RolePlay, AnchorBlockId = "P2",
            CharacterName = "Thỏ", SourceQuote = "Có ai ở đó không?", MatchFeedback = "Giỏi lắm!" };
        var wrongBlock = ok with { AnchorBlockId = "P3" };
        var result = ListeningInteractionMapper.Resolve([ok, wrongBlock], S.Content, S.Blocks);
        Assert.Equal([new ListeningValidationError("QUOTE_NOT_FOUND", 1, "lineQuote")], result.Errors);
        Assert.Equal("Có ai ở đó không?", S.Content[result.Items[0].SourceStart!.Value..result.Items[0].SourceEnd!.Value]);
    }

    [Fact]
    public void PicturePrediction_ResolvesRevealBlock()
    {
        var draft = new ListeningInteractionDraft { Type = ListeningInteractionType.PicturePrediction, AnchorBlockId = "P2",
            RevealBlockId = "P3", SourceQuote = "Thỏ nghe tiếng sột soạt sau bụi cây." };
        var result = ListeningInteractionMapper.Resolve([draft, draft with { RevealBlockId = "P99" }], S.Content, S.Blocks);
        Assert.Equal(S.Block("P3").EndOffset, result.Items[0].RevealOffset);
        Assert.Equal([new ListeningValidationError("REVEAL_BLOCK_INVALID", 1, "revealBlockId")], result.Errors);
    }

    [Fact]
    public void EventOrdering_ResolvesEachEventQuote()
    {
        var draft = new ListeningInteractionDraft
        {
            Type = ListeningInteractionType.EventOrdering, AnchorBlockId = "P4", // bị bỏ qua: luôn đặt sau đoạn cuối
            Options =
            [
                new() { Text = "Thỏ đi lạc", CorrectOrder = 1, BlockId = "P1", SourceQuote = "Thỏ con đi lạc vào rừng." },
                new() { Text = "Thỏ nghe tiếng động", CorrectOrder = 2, BlockId = "P2", SourceQuote = "Không có câu này." },
                new() { Text = "Bác Gấu dẫn Thỏ về", CorrectOrder = 3, BlockId = "P0", SourceQuote = "Bác Gấu dẫn Thỏ về tới cửa hang." }
            ]
        };
        var result = ListeningInteractionMapper.Resolve([draft], S.Content, S.Blocks);
        Assert.Equal(S.Block("P6").EndOffset, result.Items[0].AnchorOffset);
        Assert.Equal(0, result.Items[0].Options[0].SourceStart);
        Assert.Contains(new ListeningValidationError("QUOTE_NOT_FOUND", 0, "events[1].sourceQuote"), result.Errors);
        Assert.Contains(new ListeningValidationError("EVENT_ORDER_INVALID", 0, "events[2].blockId"), result.Errors);
    }

    [Fact]
    public void EntityRoundTrip_KeepsAllFields()
    {
        var resolved = ListeningInteractionMapper.Resolve(
            [S.Checkpoint("P2", CheckpointKind.Emotion, "Trời bắt đầu tối.")], S.Content, S.Blocks).Items[0];
        var (interaction, options) = ListeningInteractionMapper.ToEntities(resolved, storyVersionId: 7);
        Assert.Equal(7, interaction.StoryVersionId);
        Assert.Equal([1, 2, 3, 4], options.Select(o => o.SortOrder));
        var back = ListeningInteractionMapper.ToDraft(interaction, options, S.Blocks);
        Assert.Equal("P2", back.AnchorBlockId);
        // Record so sánh list theo tham chiếu, nên gán chung Options rồi so phần còn lại.
        Assert.Equal(resolved with { Options = back.Options }, back);
        Assert.Equal(resolved.Options, back.Options);
    }
}
