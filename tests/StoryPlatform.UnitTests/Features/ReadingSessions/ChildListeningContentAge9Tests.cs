using System.Text.Json;
using Moq;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public sealed class ChildListeningContentAge9Tests
{
    private readonly ReadingHarness _h = new();
    private readonly ChildReadingService _sut;
    private readonly ReadingSession _session;
    private readonly StoryScene _scene;

    public ChildListeningContentAge9Tests()
    {
        _sut = new ChildReadingService(_h.UnitOfWork.Object, new Mock<IChildRoadmapEligibility>().Object,
            new Mock<IChildRoadmapProgressRecorder>().Object, new Mock<IChildRewardService>().Object);
        _session = _h.AddSession();
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 9;
        _scene = _h.AddScene(0, "Story scene");
        _scene.TextRangeStart = 0;
        _scene.TextRangeEnd = 100;
    }

    private ListeningInteraction Add(int id, ListeningInteractionType type)
    {
        var item = new ListeningInteraction
        {
            Id = id, StoryVersionId = ReadingHarness.Version, StorySceneId = _scene.Id,
            TargetAge = 9, Type = type, AnchorOffset = 100, Prompt = "Mai giúp Nam."
        };
        _h.ListeningInteractions.Add(item);
        return item;
    }

    private void Option(int id, int interactionId, int sort, string text, ListeningOptionRole role,
        bool isMatch = false, string? feedback = null, string? counterpoint = null) =>
        _h.ListeningOptions.Add(new ListeningInteractionOption
        {
            Id = id, ListeningInteractionId = interactionId, SortOrder = sort, Text = text,
            Role = role, IsMatch = isMatch, Feedback = feedback, Counterpoint = counterpoint,
            ImageDescription = role == ListeningOptionRole.Node ? $"Chân dung {text}" : null
        });

    private async Task<ReadingInteractionDto?> Content() =>
        (await _sut.GetContentAsync(ReadingHarness.Child, _session.Id)).Scenes[0].Interaction;

    [Fact]
    public async Task Trial_SendsReasonsButNeverSendsBothSidesOrGradingKeys()
    {
        var trial = Add(91, ListeningInteractionType.CharacterTrial);
        Option(911, 91, 1, "Mai muốn giúp", ListeningOptionRole.None, feedback: "Mai quan tâm Nam.",
            counterpoint: "Mai cũng cần giữ sức.");
        _h.AddListeningMedia(trial);

        var dto = await Content();
        Assert.NotNull(dto);
        Assert.True(dto.AllowsFreeAnswer);
        Assert.False(dto.Graded);
        Assert.Single(dto.Reasons!);
        var json = JsonSerializer.Serialize(dto);
        Assert.DoesNotContain("Mai quan tâm Nam.", json);
        Assert.DoesNotContain("Mai cũng cần giữ sức.", json);
        Assert.DoesNotContain("IsMatch", json);

        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 8;
        Assert.Null(await Content());
    }

    [Fact]
    public async Task RelationshipMap_HidesWhenANodePictureIsMissing_AndExposesOnlyEdges()
    {
        var map = Add(92, ListeningInteractionType.RelationshipMap);
        map.RelationsJson = """{"edges":[{"from":0,"to":1,"accepted":[0],"evidence":"A giúp B.","explanation":"A giúp B."},{"from":1,"to":2,"accepted":[1],"evidence":"B bảo vệ C.","explanation":"B bảo vệ C."},{"from":2,"to":0,"accepted":[2],"evidence":"C tin A.","explanation":"C tin A."}]}""";
        Option(921, 92, 1, "A", ListeningOptionRole.Node);
        Option(922, 92, 2, "B", ListeningOptionRole.Node);
        Option(923, 92, 3, "C", ListeningOptionRole.Node);
        Option(924, 92, 4, "giúp đỡ", ListeningOptionRole.Card);
        Option(925, 92, 5, "bảo vệ", ListeningOptionRole.Card);
        Option(926, 92, 6, "tin tưởng", ListeningOptionRole.Card);
        Option(927, 92, 7, "cản trở", ListeningOptionRole.Card);
        _h.AddListeningMedia(map);

        var dto = await Content();
        Assert.Equal(3, dto!.Edges!.Count);
        Assert.Equal(3, dto.Nodes!.Count);
        Assert.Equal(4, dto.Cards!.Count);
        var json = JsonSerializer.Serialize(dto);
        Assert.DoesNotContain("accepted", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evidence", json, StringComparison.OrdinalIgnoreCase);

        _h.Assets.RemoveAll(x => x.ListeningInteractionOptionId == 921 && x.Type == MediaType.ListeningImage);
        Assert.Null(await Content());
    }
}
