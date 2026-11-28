using System.Text.Json;
using Moq;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public sealed class ChildListeningContentTests
{
    private readonly ReadingHarness _h = new();
    private readonly ChildReadingService _sut;
    private readonly ReadingSession _session;
    private readonly StoryScene[] _scenes;

    public ChildListeningContentTests()
    {
        _sut = new ChildReadingService(_h.UnitOfWork.Object, new Mock<IChildRoadmapEligibility>().Object,
            new Mock<IChildRoadmapProgressRecorder>().Object, new Mock<IChildRewardService>().Object);
        _session = _h.AddSession();
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 8;
        // 3 scene liền nhau: [0,20), [20,40), [40,60).
        _scenes = Enumerable.Range(0, 3).Select(i =>
        {
            var scene = _h.AddScene(i, $"Scene {i}");
            scene.TextRangeStart = i * 20;
            scene.TextRangeEnd = i * 20 + 20;
            return scene;
        }).ToArray();
    }

    private ListeningInteraction Add(int id, ListeningInteractionType type, int sceneIndex, Action<ListeningInteraction>? setup = null)
    {
        var interaction = new ListeningInteraction
        {
            Id = id, StoryVersionId = ReadingHarness.Version, TargetAge = 8, Type = type,
            AnchorOffset = _scenes[sceneIndex].TextRangeEnd, StorySceneId = _scenes[sceneIndex].Id,
            MatchFeedback = "Giỏi quá!", NoMatchFeedback = "Mình nghe lại nhé."
        };
        setup?.Invoke(interaction);
        _h.ListeningInteractions.Add(interaction);
        return interaction;
    }

    private void Option(int id, int interactionId, int order, string text, bool isMatch = false,
        int? correctOrder = null, int? sourceStart = null, string? feedback = "Ý hay đó!") =>
        _h.ListeningOptions.Add(new ListeningInteractionOption
        {
            Id = id, ListeningInteractionId = interactionId, SortOrder = order, Text = text, Feedback = feedback,
            IsMatch = isMatch, CorrectOrder = correctOrder, SourceStart = sourceStart, SourceQuote = $"câu {id}",
            ImageDescription = $"hình {id}"
        });

    private ListeningInteraction Checkpoint()
    {
        var interaction = Add(1, ListeningInteractionType.Checkpoint, 0, x =>
        {
            x.Kind = CheckpointKind.Emotion;
            x.Prompt = "Thỏ thấy thế nào?";
            x.Explanation = "Trời tối nên Thỏ sợ.";
        });
        Option(10, 1, 1, "Sợ", isMatch: true, feedback: "Ừ, Thỏ sợ.");
        Option(11, 1, 2, "Vui");
        _h.AddListeningMedia(interaction);
        return interaction;
    }

    private Task<ReadingContentDto> Content() => _sut.GetContentAsync(ReadingHarness.Child, _session.Id);

    [Theory]
    [InlineData(6, 2)]
    [InlineData(7, 3)]
    [InlineData(8, null)]
    public async Task Build_OnlyReturnsChildAgeSet(int age, int? expectedInteraction)
    {
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = age;
        foreach (var (id, target) in new (int, int?)[] { (1, null), (2, 6), (3, 7) })
        {
            var interaction = Add(id, ListeningInteractionType.Checkpoint, 0, x => { x.TargetAge = target; x.Prompt = "Ai đây?"; });
            Option(id * 10, id, 1, "Thỏ");
            _h.AddListeningMedia(interaction);
        }
        var content = await Content();
        Assert.Equal(expectedInteraction, content.Scenes[0].Interaction?.Id);
    }

    // ---- Tuổi 7: nội dung gửi cho bé ----
    // Fixture mặc định dùng tuổi 8 (chưa có bộ riêng); các test tuổi 7 đặt bé và tương tác sang tuổi 7.
    private ListeningInteraction Add7(int id, ListeningInteractionType type, int sceneIndex, Action<ListeningInteraction>? setup = null)
    {
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 7;
        return Add(id, type, sceneIndex, x => { x.TargetAge = 7; setup?.Invoke(x); });
    }

    private ListeningInteraction SearchInScene0(string? hotspot = "valid")
    {
        var picture = _h.AddIllustration(_scenes[0], "scene-0.png");
        picture.ValidationStatus = ValidationStatus.Passed;
        var interaction = Add7(31, ListeningInteractionType.PictureSearch, 0, x =>
        {
            x.Prompt = "Tìm Thỏ con trong tranh"; x.TargetLabel = "Thỏ con"; x.SourceStart = 5;
            x.HotspotJson = hotspot == "valid" ? new HotspotBox(picture.Id, 0.4, 0.5, 0.2, 0.2).ToJson() : hotspot;
        });
        _h.AddListeningMedia(interaction);
        return interaction;
    }

    [Fact]
    public async Task Age7PictureSearch_ShowsTheStoryPictureAndTarget_ButNeverTheBox()
    {
        SearchInScene0();
        var dto = (await Content()).Scenes[0].Interaction!;
        Assert.Equal("PictureSearch", dto.Type);
        Assert.Equal("scene-0.png", dto.SearchImageUrl);
        Assert.Equal("Thỏ con", dto.SearchTarget);
        Assert.Equal(2, dto.MaxChoices);
        Assert.NotNull(dto.AudioUrls.GetValueOrDefault(ListeningAudioParts.Prompt));
        var json = Json(dto);
        Assert.DoesNotContain("hotspot", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0.4", json);
        Assert.DoesNotContain("assetId", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    public async Task Age7PictureSearch_WithoutAUsableBox_IsHidden(string? hotspot)
    {
        SearchInScene0(hotspot);
        Assert.Null((await Content()).Scenes[0].Interaction);
    }

    [Fact]
    public async Task Age7PictureSearch_BoxOfAnotherPicture_IsHidden()
    {
        SearchInScene0(new HotspotBox(9999, 0.4, 0.5, 0.2, 0.2).ToJson());
        Assert.Null((await Content()).Scenes[0].Interaction);
    }

    [Fact]
    public async Task Age7PictureSearch_PictureNotYetValidated_IsHidden()
    {
        var interaction = SearchInScene0();
        _h.Assets.Single(x => x.Type == MediaType.Illustration).ValidationStatus = ValidationStatus.Pending;
        Assert.Null((await Content()).Scenes[0].Interaction);
        Assert.NotNull(interaction.HotspotJson);
    }

    [Fact]
    public async Task Age7TrueFalse_ShowsBothStatementsInASessionStableOrder_WithoutTheAnswer()
    {
        var interaction = Add7(32, ListeningInteractionType.TrueFalse, 1);
        Option(320, 32, 1, "Thỏ nghe tiếng động", isMatch: true);
        Option(321, 32, 2, "Thỏ ngủ trong hang");
        _h.AddListeningMedia(interaction);
        var first = (await Content()).Scenes[1].Interaction!;
        var second = (await Content()).Scenes[1].Interaction!;
        Assert.Equal(2, first.Statements!.Count);
        Assert.Equal(first.Statements.Select(x => x.Id), second.Statements!.Select(x => x.Id));
        Assert.All(first.Statements, statement => Assert.NotNull(statement.AudioUrl));
        Assert.DoesNotContain("IsMatch", Json(first));
        Assert.DoesNotContain("isTrue", Json(first), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(first.Options);
    }

    [Fact]
    public async Task Age7FollowAction_ShowsItemsAndTargetWithPictures_WithoutTheAnswer()
    {
        var interaction = Add7(33, ListeningInteractionType.FollowAction, 1, x => { x.Prompt = "Bé giúp Thỏ nhé"; x.CharacterName = "Thỏ"; });
        Option(330, 33, 1, "bác Gấu", isMatch: true);
        Option(331, 33, 2, "bụi cây");
        Option(332, 33, 3, "cửa hang");
        _h.ListeningOptions.Single(x => x.Id == 330).Role = ListeningOptionRole.Item;
        _h.ListeningOptions.Single(x => x.Id == 331).Role = ListeningOptionRole.Item;
        _h.ListeningOptions.Single(x => x.Id == 332).Role = ListeningOptionRole.Target;
        _h.AddListeningMedia(interaction);
        var dto = (await Content()).Scenes[1].Interaction!;
        Assert.Equal(["bác Gấu", "bụi cây"], dto.ActionItems!.Select(x => x.Name));
        Assert.Equal("cửa hang", dto.ActionTarget!.Name);
        Assert.All(dto.ActionItems.Append(dto.ActionTarget), card =>
        {
            Assert.NotNull(card.ImageUrl);
            Assert.NotNull(card.AudioUrl);
        });
        Assert.DoesNotContain("IsMatch", Json(dto));
    }

    [Fact]
    public async Task Age7FollowAction_HiddenWhenAnyCardPictureIsMissing()
    {
        var interaction = Add7(33, ListeningInteractionType.FollowAction, 1, x => x.Prompt = "Bé giúp Thỏ nhé");
        Option(330, 33, 1, "bác Gấu", isMatch: true);
        Option(332, 33, 2, "cửa hang");
        _h.ListeningOptions.Single(x => x.Id == 330).Role = ListeningOptionRole.Item;
        _h.ListeningOptions.Single(x => x.Id == 332).Role = ListeningOptionRole.Target;
        _h.AddListeningMedia(interaction);
        _h.Assets.RemoveAll(x => x.Type == MediaType.ListeningImage && x.ListeningInteractionOptionId == 332);
        Assert.Null((await Content()).Scenes[1].Interaction);
    }

    private ListeningInteraction BuilderInScene1()
    {
        var interaction = Add7(34, ListeningInteractionType.SentenceBuilder, 1, x =>
        { x.SourceQuote = "Bác Gấu dẫn Thỏ về hang."; x.AcceptedOrdersJson = "[[1,2,3],[2,1,3]]"; });
        Option(340, 34, 1, "Bác Gấu", correctOrder: 1);
        Option(341, 34, 2, "dẫn Thỏ", correctOrder: 2);
        Option(342, 34, 3, "về hang", correctOrder: 3);
        _h.AddListeningMedia(interaction);
        return interaction;
    }

    [Fact]
    public async Task Age7SentenceBuilder_ShowsShuffledCardsNeverInStoryOrder_WithoutTheOrders()
    {
        BuilderInScene1();
        var dto = (await Content()).Scenes[1].Interaction!;
        Assert.Equal(3, dto.SlotCount);
        Assert.Equal(3, dto.WordCards!.Count);
        Assert.NotEqual([340, 341, 342], dto.WordCards.Select(x => x.Id));
        Assert.All(dto.WordCards, card => Assert.NotNull(card.AudioUrl));
        Assert.Equal(("SentenceBuilder", 7, 2), (dto.Type, dto.TargetAge, dto.MaxVoiceRetries));
        var json = Json(dto);
        Assert.DoesNotContain("acceptedOrders", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correctOrder", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[[1,2,3]", json);
    }

    [Fact]
    public async Task Age7SentenceBuilder_OrderIsStableWithinASession()
    {
        BuilderInScene1();
        var first = (await Content()).Scenes[1].Interaction!.WordCards!.Select(x => x.Id).ToArray();
        var second = (await Content()).Scenes[1].Interaction!.WordCards!.Select(x => x.Id).ToArray();
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Age7SentenceBuilder_NeedsAudioForEveryAcceptedSentence()
    {
        var interaction = BuilderInScene1();
        _h.Assets.RemoveAll(x => x.AudioPartKey == "sentence:2");
        Assert.Null((await Content()).Scenes[1].Interaction);
        Assert.NotNull(interaction);
    }

    [Fact]
    public async Task Age7EventOrdering_IsShownMidStoryWithTheSceneIllustrations()
    {
        var picture = _h.AddIllustration(_scenes[0], "scene-0.png");
        picture.ValidationStatus = ValidationStatus.Passed;
        var interaction = Add7(35, ListeningInteractionType.EventOrdering, 1);
        Option(350, 35, 1, "Thỏ lạc", correctOrder: 1, sourceStart: 5);
        Option(351, 35, 2, "Gấu giúp", correctOrder: 2, sourceStart: 25);
        Option(352, 35, 3, "Về nhà", correctOrder: 3, sourceStart: 45);
        _h.AddListeningMedia(interaction);
        var content = await Content();
        var dto = content.Scenes[1].Interaction!;
        Assert.Equal("EventOrdering", dto.Type);
        Assert.Null(content.Scenes[2].Interaction);                       // không bị đẩy xuống scene cuối
        Assert.Equal("scene-0.png", dto.Options.Single(o => o.Id == 350).ImageUrl);
        Assert.NotEqual([350, 351, 352], dto.Options.Select(o => o.Id));
    }

    [Fact]
    public async Task Age7TrueFalse_CountsAsAttemptedOnlyAfterBothStatements()
    {
        var interaction = Add7(32, ListeningInteractionType.TrueFalse, 1);
        Option(320, 32, 1, "Thỏ nghe tiếng động", isMatch: true);
        Option(321, 32, 2, "Thỏ ngủ trong hang");
        _h.AddListeningMedia(interaction);
        _h.ListeningAttempts.Add(new ListeningInteractionAttempt
        { Id = 1, ReadingSessionId = _session.Id, ListeningInteractionId = 32, SelectedOptionId = 320, IsMatch = true });
        Assert.False((await Content()).Scenes[1].AttemptedInSession);
        _h.ListeningAttempts.Add(new ListeningInteractionAttempt
        { Id = 2, ReadingSessionId = _session.Id, ListeningInteractionId = 32, SelectedOptionId = 321, IsMatch = false });
        _h.ListeningAttempts.Add(new ListeningInteractionAttempt
        { Id = 3, ReadingSessionId = _session.Id, ListeningInteractionId = 32, SelectedOptionId = 321, IsMatch = false });
        Assert.True((await Content()).Scenes[1].AttemptedInSession);
    }

    [Fact]
    public async Task Age7EventOrdering_CountsAsAttemptedAfterTwoWrongOrders()
    {
        var interaction = Add7(35, ListeningInteractionType.EventOrdering, 1);
        Option(350, 35, 1, "Thỏ lạc", correctOrder: 1, sourceStart: 5);
        Option(351, 35, 2, "Gấu giúp", correctOrder: 2, sourceStart: 25);
        Option(352, 35, 3, "Về nhà", correctOrder: 3, sourceStart: 45);
        _h.AddListeningMedia(interaction);
        _h.ListeningAttempts.Add(new ListeningInteractionAttempt { Id = 1, ReadingSessionId = _session.Id, ListeningInteractionId = 35, IsMatch = false });
        Assert.False((await Content()).Scenes[1].AttemptedInSession);
        _h.ListeningAttempts.Add(new ListeningInteractionAttempt { Id = 2, ReadingSessionId = _session.Id, ListeningInteractionId = 35, IsMatch = false });
        Assert.True((await Content()).Scenes[1].AttemptedInSession);
    }

    [Fact]
    public async Task Age7Child_SeesTheAge7SetOnly_AndAge6ChildSeesTheAge6Set()
    {
        var age7 = Add7(36, ListeningInteractionType.TrueFalse, 1);
        Option(360, 36, 1, "Thỏ nghe tiếng động", isMatch: true);
        Option(361, 36, 2, "Thỏ ngủ trong hang");
        _h.AddListeningMedia(age7);
        var age6 = Add7(37, ListeningInteractionType.Checkpoint, 1, x => { x.TargetAge = 6; x.Kind = CheckpointKind.Literal; x.Prompt = "Ai đi lạc?"; });
        Option(370, 37, 1, "Thỏ", isMatch: true, feedback: "Đúng rồi!");
        Option(371, 37, 2, "Gấu", feedback: "Xem lại nhé.");
        _h.AddListeningMedia(age6);
        Assert.Equal(36, (await Content()).Scenes[1].Interaction!.Id);
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 6;
        Assert.Equal(37, (await Content()).Scenes[1].Interaction!.Id);
    }

    [Fact]
    public async Task Literal_CardsHavePictureAndText_WithoutRevealingTheAnswer()
    {
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 6;
        var interaction = Add(1, ListeningInteractionType.Checkpoint, 0, x =>
        {
            x.TargetAge = 6; x.Kind = CheckpointKind.Literal; x.Prompt = "Ai đi lạc vào rừng?";
        });
        Option(10, 1, 1, "Thỏ con", isMatch: true, feedback: "Đúng là Thỏ con!");
        Option(11, 1, 2, "bác Gấu", feedback: "Mình xem lại nhé.");
        _h.AddListeningMedia(interaction);
        var dto = (await Content()).Scenes[0].Interaction!;
        Assert.Equal(["Thỏ con", "bác Gấu"], dto.Options.Select(o => o.Text));
        Assert.Equal(["picture-10.png", "picture-11.png"], dto.Options.Select(o => o.ImageUrl));
        Assert.All(dto.Options, o => Assert.NotNull(o.AudioUrl));
        Assert.All(dto.Options, o => Assert.Null(o.Feedback));
        Assert.All(dto.Options, o => Assert.Null(o.FeedbackAudioUrl));
        Assert.DoesNotContain("IsMatch", Json(dto));
    }

    [Fact]
    public async Task Literal_HiddenUntilEveryOptionPictureIsReady()
    {
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 6;
        var interaction = Add(1, ListeningInteractionType.Checkpoint, 0, x =>
        {
            x.TargetAge = 6; x.Kind = CheckpointKind.Literal; x.Prompt = "Ai đi lạc vào rừng?";
        });
        Option(10, 1, 1, "Thỏ con", isMatch: true);
        Option(11, 1, 2, "bác Gấu");
        _h.AddListeningMedia(interaction);
        _h.Assets.RemoveAll(a => a.Type == MediaType.ListeningImage && a.ListeningInteractionOptionId == 11);
        Assert.Null((await Content()).Scenes[0].Interaction);
    }

    [Fact]
    public async Task RolePlayAge6_ExposesWordTimingsOnlyForSpokenLine()
    {
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 6;
        var interaction = Add(1, ListeningInteractionType.RolePlay, 0, x =>
        {
            x.TargetAge = 6; x.CharacterName = "Thỏ"; x.SourceQuote = "Có ai ở đó không";
        });
        _h.AddListeningMedia(interaction);
        const string timings = "[{\"word\":\"Có\",\"start\":0.0,\"end\":0.4},{\"word\":\"ai\",\"start\":0.4,\"end\":0.8}]";
        foreach (var asset in _h.Assets.Where(a => a.ListeningInteractionId == 1 && a.Type == MediaType.ListeningAudio &&
                     a.AudioPartKey is ListeningAudioParts.Text or ListeningAudioParts.Match))
            asset.WordTimings = timings;
        var dto = (await Content()).Scenes[0].Interaction!;
        Assert.Equal([ListeningAudioParts.Text], dto.AudioWordTimings.Keys);
        Assert.Equal(timings, dto.AudioWordTimings[ListeningAudioParts.Text]);
        Assert.True(dto.AudioUrls.ContainsKey(ListeningAudioParts.Match));
    }

    [Fact]
    public async Task ProviderWithoutTimings_ReturnsEmptyWordTimings()
    {
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 6;
        var interaction = Add(1, ListeningInteractionType.RolePlay, 0, x =>
        {
            x.TargetAge = 6; x.CharacterName = "Thỏ"; x.SourceQuote = "Có ai ở đó không";
        });
        _h.AddListeningMedia(interaction);
        var dto = (await Content()).Scenes[0].Interaction!;
        Assert.Empty(dto.AudioWordTimings);
    }

    [Fact]
    public async Task CharacterLetter_MasksSingleUnit_AndDoesNotExposeAnswer()
    {
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 6;
        var interaction = Add(1, ListeningInteractionType.CharacterLetter, 0, x =>
        {
            x.TargetAge = 6; x.CharacterName = "Thỏ"; x.BlankIndex = 0;
        });
        Option(10, 1, 1, "th", isMatch: true);
        Option(11, 1, 2, "b");
        Option(12, 1, 3, "c");
        _h.AddListeningMedia(interaction);
        var dto = (await Content()).Scenes[0].Interaction!;
        Assert.Equal(new string?[] { null, "ỏ" }, dto.NameUnits);
        Assert.Equal("_ỏ", dto.MaskedName);
        Assert.Equal("picture-10.png", dto.CharacterImageUrl);
        Assert.Equal(2, dto.MaxChoices);
        Assert.Equal(2, dto.MaxVoiceRetries);
        Assert.Equal(3, dto.LetterOptions!.Count);
        Assert.All(dto.LetterOptions, option => Assert.NotNull(option.SoundAudioUrl));
        Assert.DoesNotContain("IsMatch", Json(dto));
    }

    [Fact]
    public async Task CharacterLetter_PortraitAndLetterSoundsAreSigned()
    {
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 6;
        var interaction = Add(1, ListeningInteractionType.CharacterLetter, 0, x =>
        {
            x.TargetAge = 6; x.CharacterName = "Thỏ"; x.BlankIndex = 0;
        });
        Option(10, 1, 1, "th", isMatch: true);
        Option(11, 1, 2, "b");
        Option(12, 1, 3, "c");
        _h.AddListeningMedia(interaction);
        var content = await Content();
        await new PortraitSigner().SignAsync(content, CancellationToken.None);
        var dto = content.Scenes[0].Interaction!;
        Assert.Equal("signed:picture-10.png", dto.CharacterImageUrl);
        Assert.All(dto.LetterOptions!, option => Assert.StartsWith("signed:", option.SoundAudioUrl));
    }

    private sealed class PortraitSigner : IChildMediaUrlSigner
    {
        public Task<Func<string?, string?>> SignAsync(IEnumerable<string?> paths, CancellationToken cancellationToken = default)
        {
            var allowed = paths.OfType<string>().ToHashSet();
            return Task.FromResult<Func<string?, string?>>(path => path is not null && allowed.Contains(path) ? "signed:" + path : null);
        }
    }

    [Fact]
    public async Task WhoSaidIt_HasLineAndSpeakerPictures()
    {
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 6;
        var interaction = Add(1, ListeningInteractionType.WhoSaidIt, 0, x => { x.TargetAge = 6; x.SourceQuote = "Chào bạn!"; });
        Option(10, 1, 1, "Thỏ", isMatch: true);
        Option(11, 1, 2, "Gấu");
        _h.AddListeningMedia(interaction);
        var dto = (await Content()).Scenes[0].Interaction!;
        Assert.Equal("Chào bạn!", dto.SpeakerLine);
        Assert.Equal(2, dto.Speakers!.Count);
        Assert.All(dto.Speakers, speaker => { Assert.NotNull(speaker.ImageUrl); Assert.NotNull(speaker.NameAudioUrl); });
        Assert.DoesNotContain("IsMatch", Json(dto));
    }

    [Fact]
    public async Task Age6Picture_IsUngradedWithoutReveal()
    {
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 6;
        var interaction = Add(1, ListeningInteractionType.PicturePrediction, 0, x => { x.TargetAge = 6; x.Prompt = "Chuyện gì tiếp theo?"; });
        Option(10, 1, 1, "Thỏ chạy");
        Option(11, 1, 2, "Gấu chạy");
        _h.AddListeningMedia(interaction);
        var dto = (await Content()).Scenes[0].Interaction!;
        Assert.False(dto.Graded);
        Assert.Equal(1, dto.MaxChoices);
        Assert.Null(dto.RevealSceneIndex);
    }

    // Giữ nguyên chữ có dấu; encoder mặc định đổi sang \uXXXX làm phép kiểm "không chứa" luôn đạt.
    private static string Json(object? value) => JsonSerializer.Serialize(value,
        new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    [Fact]
    public async Task Checkpoint_IsOnItsSceneWithAudioAndCards_ButNoAnswers()
    {
        Checkpoint();

        var content = await Content();

        var item = content.Scenes[0].Interaction!;
        Assert.Equal(1, item.Id);
        Assert.Equal("Checkpoint", item.Type);
        Assert.Equal("Emotion", item.Kind);
        Assert.Equal("Thỏ thấy thế nào?", item.Prompt);
        Assert.Equal(new[] { "noMatch", "prompt" }, item.AudioUrls.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "Sợ", "Vui" }, item.Options.Select(x => x.Text));
        Assert.Equal("Ừ, Thỏ sợ.", item.Options[0].Feedback);
        Assert.Equal("listen-1-10-feedback.mp3", item.Options[0].FeedbackAudioUrl);
        Assert.Equal("listen-1-10-text.mp3", item.Options[0].AudioUrl);
        Assert.Null(content.Scenes[1].Interaction);
        Assert.False(content.Scenes[0].AttemptedInSession);
        Assert.False(content.ListenOnly);
        var json = Json(content);
        Assert.Contains("Thỏ thấy thế nào?", json);
        Assert.DoesNotContain("IsMatch", json);
        Assert.DoesNotContain("Explanation", json);
        Assert.DoesNotContain("Trời tối nên Thỏ sợ", json);
    }

    [Fact]
    public async Task InteractionWithMissingMediaOrWithoutScene_IsNotReturned()
    {
        Checkpoint();
        _h.Assets.Single(x => x.ListeningInteractionId == 1 && x.AudioPartKey == "prompt").Status = MediaStatus.ManualReview;
        var unbound = Add(2, ListeningInteractionType.RolePlay, 1, x => x.StorySceneId = null);
        _h.AddListeningMedia(unbound);

        var content = await Content();

        Assert.All(content.Scenes, scene => Assert.Null(scene.Interaction));
    }

    [Fact]
    public async Task FillBlank_ShowsMaskedSentenceAndHint_WithoutAcceptedAnswers()
    {
        var interaction = Add(3, ListeningInteractionType.FillBlank, 0, x =>
        {
            x.SourceQuote = "Thỏ gặp Bác Gấu trong rừng.";
            x.HiddenWord = "bác Gấu";
            x.Hint = "Một con vật to lớn.";
        });
        Option(30, 3, 1, "bác Gấu");
        Option(31, 3, 2, "gấu");
        _h.AddListeningMedia(interaction);

        var item = (await Content()).Scenes[0].Interaction!;

        Assert.Equal("Thỏ gặp ____ trong rừng.", item.MaskedSentence);
        Assert.Equal("Một con vật to lớn.", item.Hint);
        Assert.Equal("listen-3-i-text.mp3", item.AudioUrls["text"]);
        Assert.Empty(item.Options);
        var json = Json(item);
        Assert.Contains("Thỏ gặp ____ trong rừng.", json);
        Assert.DoesNotContain("Gấu", json);
    }

    [Fact]
    public async Task FillBlank_WithoutShortSentenceAudio_IsHidden()
    {
        var interaction = Add(3, ListeningInteractionType.FillBlank, 0, x =>
        {
            x.SourceQuote = "Thỏ gặp Bác Gấu trong rừng.";
            x.HiddenWord = "bác Gấu";
            x.Hint = "Một con vật to lớn.";
        });
        Option(30, 3, 1, "bác Gấu");
        _h.AddListeningMedia(interaction);
        _h.Assets.RemoveAll(x => x.ListeningInteractionId == 3 && x.AudioPartKey == "text");

        Assert.Null((await Content()).Scenes[0].Interaction);
    }

    [Fact]
    public async Task RolePlay_UsesAudioOfTheSegmentHoldingTheLine()
    {
        var interaction = Add(4, ListeningInteractionType.RolePlay, 1, x =>
        {
            x.CharacterName = "Thỏ";
            x.SourceQuote = "Có ai không?";
            x.SourceStart = 32;
        });
        _h.AddListeningMedia(interaction);
        var first = _h.AddSegment(_scenes[1], 1, "Thỏ nghe.");
        first.StartOffset = 0; first.EndOffset = 10;
        var second = _h.AddSegment(_scenes[1], 2, "Có ai không?");
        second.StartOffset = 10; second.EndOffset = 20;
        _h.AddAudio(first, "seg-1.mp3");
        _h.AddAudio(second, "seg-2.mp3");

        var item = (await Content()).Scenes[1].Interaction!;

        Assert.Equal("seg-2.mp3", item.ModelAudioUrl);
        Assert.Equal("Có ai không?", item.ModelSentence);
        Assert.Equal("Thỏ", item.CharacterName);
        Assert.Equal("Em hãy đóng vai Thỏ, nghe mẫu rồi nói theo câu này nhé!", item.Instruction);
        Assert.Equal(new[] { "instruction", "match" }, item.AudioUrls.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("listen-4-i-instruction.mp3", item.AudioUrls["instruction"]);
    }

    [Fact]
    public async Task RolePlay_CreatedBeforeInstructionAudio_IsStillReturnedWithInstructionText()
    {
        // Tuổi chưa có bộ riêng (9): dòng cũ tạo trước khi có câu hướng dẫn vẫn hiện, không đòi audio câu hướng dẫn.
        _h.Children.Single(x => x.Id == ReadingHarness.Child).Age = 9;
        var interaction = Add(4, ListeningInteractionType.RolePlay, 1, x =>
        {
            x.TargetAge = 9;
            x.CharacterName = "Thỏ";
            x.SourceQuote = "Có ai không?";
            x.SourceStart = 32;
        });
        _h.AddListeningMedia(interaction);
        _h.Assets.RemoveAll(x => x.ListeningInteractionId == 4 && x.AudioPartKey == "instruction");
        var segment = _h.AddSegment(_scenes[1], 1, "Có ai không?");
        segment.StartOffset = 10; segment.EndOffset = 20;
        _h.AddAudio(segment, "seg-1.mp3");

        var item = (await Content()).Scenes[1].Interaction!;

        Assert.Equal("Em hãy đóng vai Thỏ, nghe mẫu rồi nói theo câu này nhé!", item.Instruction);
        Assert.False(item.AudioUrls.ContainsKey("instruction"));
    }

    [Fact]
    public async Task RolePlayWithoutSegmentAudio_IsNotReturned()
    {
        var interaction = Add(4, ListeningInteractionType.RolePlay, 1, x => x.SourceStart = 32);
        _h.AddListeningMedia(interaction);

        Assert.Null((await Content()).Scenes[1].Interaction);
    }

    [Fact]
    public async Task PicturePrediction_HasImagesAndRevealScene()
    {
        var interaction = Add(5, ListeningInteractionType.PicturePrediction, 0, x =>
        {
            x.Prompt = "Ai đến?";
            x.WaitFeedback = "Nghe tiếp nhé!";
            x.RevealOffset = 40;
        });
        Option(50, 5, 1, "Bác Gấu", isMatch: true);
        Option(51, 5, 2, "Chim Sẻ");
        _h.AddListeningMedia(interaction);

        var item = (await Content()).Scenes[0].Interaction!;

        Assert.Equal(1, item.RevealSceneIndex);
        Assert.Equal("Nghe tiếp nhé!", item.WaitFeedback);
        Assert.Equal(new[] { "picture-50.png", "picture-51.png" }, item.Options.Select(x => x.ImageUrl));
        Assert.Equal(new[] { "prompt", "wait" }, item.AudioUrls.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task EventOrdering_CardsAreShuffledStablyAndNeverInTheRightOrder()
    {
        var interaction = Add(6, ListeningInteractionType.EventOrdering, 2);
        Option(61, 6, 1, "Thỏ lạc", correctOrder: 1, sourceStart: 5, feedback: null);
        Option(62, 6, 2, "Gấu giúp", correctOrder: 2, sourceStart: 25, feedback: null);
        Option(63, 6, 3, "Về nhà", correctOrder: 3, sourceStart: 45, feedback: null);
        _h.AddListeningMedia(interaction);

        for (var sessionId = 1; sessionId <= 20; sessionId++)
        {
            _session.Id = sessionId;
            var first = (await Content()).Scenes[2].Interaction!.Options.Select(x => x.Id).ToList();
            var again = (await Content()).Scenes[2].Interaction!.Options.Select(x => x.Id).ToList();
            Assert.Equal(first, again);
            Assert.NotEqual(new[] { 61, 62, 63 }, first);
            Assert.Equal(new[] { 61, 62, 63 }, first.Order());
        }
        var json = Json((await Content()).Scenes[2].Interaction);
        Assert.Contains("Thỏ lạc", json);
        Assert.DoesNotContain("CorrectOrder", json);
        Assert.DoesNotContain("câu 61", json);
    }

    [Fact]
    public async Task EventOrdering_CardsShowTheIllustrationOfTheirScene()
    {
        var interaction = Add(6, ListeningInteractionType.EventOrdering, 2);
        Option(61, 6, 1, "Thỏ lạc", correctOrder: 1, sourceStart: 15, feedback: null);
        Option(62, 6, 2, "Gấu giúp", correctOrder: 2, sourceStart: 25, feedback: null);
        Option(63, 6, 3, "Về nhà", correctOrder: 3, sourceStart: 45, feedback: null);
        _h.AddListeningMedia(interaction);
        // Scene 0 có 2 beat; câu của thẻ 61 (offset 15) nằm ở beat 2. Scene 1 chỉ có tranh cả scene. Scene 2 chưa có tranh.
        _h.AddBeatIllustration(_scenes[0], 1, 0, 10, "beat-1.png");
        _h.AddBeatIllustration(_scenes[0], 2, 10, 20, "beat-2-old.png", current: false);
        _h.AddBeatIllustration(_scenes[0], 2, 10, 20, "beat-2.png");
        _h.AddIllustration(_scenes[1], "scene-1.png");

        var cards = (await Content()).Scenes[2].Interaction!.Options.ToDictionary(x => x.Id, x => x.ImageUrl);

        Assert.Equal("beat-2.png", cards[61]);
        Assert.Equal("scene-1.png", cards[62]);
        Assert.Null(cards[63]);
    }

    [Fact]
    public async Task AttemptedInSession_CountsOnlyThisSession_AndEventOrderingNeedsMatchOrThreeWrong()
    {
        Checkpoint();
        var ordering = Add(6, ListeningInteractionType.EventOrdering, 2);
        Option(61, 6, 1, "A", correctOrder: 1, sourceStart: 5, feedback: null);
        Option(62, 6, 2, "B", correctOrder: 2, sourceStart: 25, feedback: null);
        _h.AddListeningMedia(ordering);
        void Attempt(int sessionId, int interactionId, bool? isMatch) => _h.ListeningAttempts.Add(new ListeningInteractionAttempt
        {
            Id = _h.ListeningAttempts.Count + 1, ReadingSessionId = sessionId, ChildProfileId = ReadingHarness.Child,
            ListeningInteractionId = interactionId, IsMatch = isMatch
        });
        Attempt(99, 1, true);
        Attempt(_session.Id, 6, false);
        Attempt(_session.Id, 6, false);

        var before = await Content();
        Assert.False(before.Scenes[0].AttemptedInSession);
        Assert.False(before.Scenes[2].AttemptedInSession);

        Attempt(_session.Id, 1, true);
        Attempt(_session.Id, 6, false);
        var after = await Content();
        Assert.True(after.Scenes[0].AttemptedInSession);
        Assert.True(after.Scenes[2].AttemptedInSession);
    }

    [Fact]
    public async Task ListenOnly_ComesFromTheStoryProgress()
    {
        Checkpoint();
        _h.Progress.Add(new ReadingProgress
        {
            Id = 1, ChildProfileId = ReadingHarness.Child, StoryId = ReadingHarness.Story, ListenOnly = true
        });

        var content = await Content();

        Assert.True(content.ListenOnly);
        Assert.NotNull(content.Scenes[0].Interaction);
    }

    [Fact]
    public void Mask_ReplacesFirstMatchIgnoringCase_AndKeepsSentenceWhenWordIsMissing()
    {
        Assert.Equal("Thỏ gặp ____ và bác Gấu.", ChildListeningContent.Mask("Thỏ gặp Bác Gấu và bác Gấu.", "bác gấu"));
        Assert.Equal("Thỏ chạy.", ChildListeningContent.Mask("Thỏ chạy.", "Gấu"));
    }
    [Fact]
    public async Task SetListenOnly_SavesPerStory_KeepsOtherFlags_AndShowsInContent()
    {
        _h.Progress.Add(new ReadingProgress
        {
            Id = 1, ChildProfileId = ReadingHarness.Child, StoryId = ReadingHarness.Story, IsBookmarked = true
        });

        var flags = await _sut.SetListenOnlyAsync(ReadingHarness.Child, _session.Id, true);

        Assert.True(flags.ListenOnly);
        Assert.True(flags.IsBookmarked);
        Assert.True(_h.Progress.Single().ListenOnly);
        Assert.True((await Content()).ListenOnly);
        Assert.Empty(_h.Telemetry);

        Assert.False((await _sut.SetListenOnlyAsync(ReadingHarness.Child, _session.Id, false)).ListenOnly);
    }

    [Fact]
    public async Task SetListenOnly_CreatesProgressForANewStory()
    {
        var flags = await _sut.SetListenOnlyAsync(ReadingHarness.Child, _session.Id, true);

        Assert.True(flags.ListenOnly);
        var progress = Assert.Single(_h.Progress);
        Assert.Equal(ReadingHarness.Story, progress.StoryId);
    }

    [Fact]
    public async Task SetListenOnly_OnAnotherChildsSession_IsNotFound() =>
        await Assert.ThrowsAsync<StoryPlatform.Application.Common.Exceptions.NotFoundException>(() =>
            _sut.SetListenOnlyAsync(ReadingHarness.OtherChild, _session.Id, true));
    private ListeningInteraction ReadAloudPractice()
    {
        var practice = Add(4, ListeningInteractionType.ReadAloud, 2, x =>
        {
            x.SourceQuote = "Bác Gấu dẫn Thỏ về tới cửa hang.";
            x.MatchFeedback = "Con đọc to rõ lắm!";
            x.NoMatchFeedback = null;
        });
        _h.AddListeningMedia(practice);
        return practice;
    }

    [Fact]
    public async Task ReadAloud_IsReturnedSeparately_AndDoesNotReplaceEventOrderingOnTheLastScene()
    {
        var ordering = Add(3, ListeningInteractionType.EventOrdering, 2);
        Option(30, 3, 1, "Thỏ đi lạc", correctOrder: 1, sourceStart: 0);
        Option(31, 3, 2, "Gấu giúp Thỏ", correctOrder: 2, sourceStart: 20);
        Option(32, 3, 3, "Thỏ về nhà", correctOrder: 3, sourceStart: 40);
        _h.AddListeningMedia(ordering);
        ReadAloudPractice();

        var content = await Content();

        Assert.Equal("EventOrdering", content.Scenes[2].Interaction!.Type);
        var practice = content.ReadAloudPractice!;
        Assert.Equal(4, practice.InteractionId);
        Assert.Equal("Bác Gấu dẫn Thỏ về tới cửa hang.", practice.Text);
        Assert.Equal("listen-4-i-text.mp3", practice.AudioUrl);
        Assert.Equal("Con đọc to rõ lắm!", practice.PraiseFeedback);
        Assert.Equal("listen-4-i-match.mp3", practice.PraiseAudioUrl);
    }

    [Fact]
    public async Task ReadAloud_WithoutPassageAudio_IsNotReturned()
    {
        ReadAloudPractice();
        _h.Assets.Single(x => x.ListeningInteractionId == 4 && x.AudioPartKey == "text").Status = MediaStatus.ManualReview;

        var content = await Content();

        Assert.Null(content.ReadAloudPractice);
        Assert.All(content.Scenes, scene => Assert.Null(scene.Interaction));
    }
}
