using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ListeningInteractions;

public sealed class ListeningAudioPartsTests
{
    private static ListeningInteractionOption Option(int id, int interactionId, int order, string text,
        string? feedback = null, bool deleted = false) => new()
    {
        Id = id, ListeningInteractionId = interactionId, SortOrder = order, Text = text,
        Feedback = feedback, IsDeleted = deleted
    };
    private static string[] Keys(IReadOnlyList<ListeningAudioPart> parts) =>
        parts.Select(x => $"{(x.OptionId is int id ? $"o{id}" : "i")}:{x.PartKey}:{x.Text}").ToArray();

    [Fact]
    public void Checkpoint_ReadsPromptNoMatchAndOptionTextFeedback()
    {
        var interaction = new ListeningInteraction { Id = 1, Type = ListeningInteractionType.Checkpoint,
            Prompt = "Thỏ thấy sao?", NoMatchFeedback = "Ý hay đó." };
        Assert.Equal(new[] { "i:prompt:Thỏ thấy sao?", "i:noMatch:Ý hay đó.",
            "o10:text:Vui", "o10:feedback:Thỏ vui lắm.", "o11:text:Buồn", "o11:feedback:Thỏ không buồn." },
            Keys(ListeningAudioParts.For(interaction,
                [Option(11, 1, 2, "Buồn", "Thỏ không buồn."), Option(10, 1, 1, "Vui", "Thỏ vui lắm.")])));
    }

    [Fact]
    public void RolePlay_ReadsOnlyMatch() =>
        Assert.Equal(["i:match:Giỏi quá!"], Keys(ListeningAudioParts.For(
            new ListeningInteraction { Id = 2, Type = ListeningInteractionType.RolePlay, MatchFeedback = "Giỏi quá!" }, [])));

    [Fact]
    public void FillBlank_DoesNotReadAnswers() =>
        Assert.Equal(["i:hint:Một con vật.", "i:match:Đúng rồi!", "i:noMatch:Thử nghe lại nhé."], Keys(
            ListeningAudioParts.For(new ListeningInteraction { Id = 3, Type = ListeningInteractionType.FillBlank,
                Hint = "Một con vật.", MatchFeedback = "Đúng rồi!", NoMatchFeedback = "Thử nghe lại nhé." },
                [Option(30, 3, 1, "thỏ")])));

    [Fact]
    public void EventOrdering_ReadsCardTextWithoutFeedback() =>
        Assert.Equal(["i:match:Đúng rồi!", "i:noMatch:Xếp lại nhé.", "o40:text:Thỏ lạc đường"], Keys(
            ListeningAudioParts.For(new ListeningInteraction { Id = 4, Type = ListeningInteractionType.EventOrdering,
                MatchFeedback = "Đúng rồi!", NoMatchFeedback = "Xếp lại nhé." },
                [Option(40, 4, 1, "Thỏ lạc đường", "không đọc")])));

    [Fact]
    public void PicturePrediction_ReadsPromptWaitAndOptionTextFeedback() =>
        Assert.Equal(["i:prompt:Ai đến?", "i:wait:Nghe tiếp nhé!", "o50:text:Bác Gấu", "o50:feedback:Đúng là bác Gấu."], Keys(
            ListeningAudioParts.For(new ListeningInteraction { Id = 5, Type = ListeningInteractionType.PicturePrediction,
                Prompt = "Ai đến?", WaitFeedback = "Nghe tiếp nhé!" },
                [Option(50, 5, 1, "Bác Gấu", "Đúng là bác Gấu.")])));

    [Fact]
    public void SkipsBlankDeletedAndUnrelatedOptions() =>
        Assert.Equal(["i:prompt:Thỏ thấy sao?", "o10:text:Vui"], Keys(ListeningAudioParts.For(
            new ListeningInteraction { Id = 1, Type = ListeningInteractionType.Checkpoint,
                Prompt = "Thỏ thấy sao?", NoMatchFeedback = "  " },
            [Option(10, 1, 1, "Vui"), Option(11, 1, 2, "Buồn", "x", deleted: true),
             Option(99, 9, 1, "Khác", "y")])));
}
