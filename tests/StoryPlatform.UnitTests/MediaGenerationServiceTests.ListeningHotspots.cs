using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.MediaStorage.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed partial class MediaGenerationServiceTests
{
    // Mỗi test dùng Id tương tác riêng: thời gian chờ thử lại của worker lưu theo Id nên dùng chung sẽ lẫn giữa các test.
    private sealed class FakeLocator(LocatedObject? answer) : IPictureObjectLocator
    {
        public List<string> Labels { get; } = [];
        public Task<LocatedObject?> LocateAsync(GeneratedMedia picture, string objectLabel, CancellationToken ct = default)
        {
            Labels.Add(objectLabel);
            return Task.FromResult(answer);
        }
    }

    private sealed class ReadableStorage : IMediaStorage
    {
        public Task<GeneratedMedia> ReadAsync(string storagePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GeneratedMedia([1, 2, 3], "image/png"));
        public Task<string> UploadAsync(string storagePath, Stream content, string mimeType, CancellationToken cancellationToken = default) =>
            Task.FromResult(storagePath);
        public Task<string> GetSignedUrlAsync(string storagePath, TimeSpan expiry, CancellationToken cancellationToken = default) =>
            Task.FromResult(storagePath);
        public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static readonly LocatedObject Found = new(0.40, 0.50, 0.20, 0.20, 0.9);

    // Một scene [0,3) có một beat và một tranh minh họa đã duyệt; Id 600/601/602.
    private static MediaAsset SeedPicture(StoryReviewServiceTests.FakeUnitOfWork uow)
    {
        uow.Seed(new StoryScene { Id = 600, StoryVersionId = 10, SceneIndex = 0, TextRangeStart = 0, TextRangeEnd = 3 });
        uow.Seed(new IllustrationBeat { Id = 601, StorySceneId = 600, BeatOrder = 1, StartOffset = 0, EndOffset = 3, VisualFocus = "x" });
        var picture = new MediaAsset
        {
            Id = 602, StoryVersionId = 10, StorySceneId = 600, IllustrationBeatId = 601, Type = MediaType.Illustration,
            Status = MediaStatus.Ready, ValidationStatus = ValidationStatus.Passed, IsCurrent = true, Url = "scene-0.png"
        };
        uow.Seed(picture);
        return picture;
    }

    private static ListeningInteraction SeedSearch(StoryReviewServiceTests.FakeUnitOfWork uow, int id, string? label = "Thỏ",
        int? packageId = null, bool deleted = false, int? sceneId = 600, string? hotspot = null)
    {
        var interaction = new ListeningInteraction
        {
            Id = id, StoryVersionId = 10, TargetAge = 7, Type = ListeningInteractionType.PictureSearch, AnchorOffset = FirstBlockEnd,
            SourceStart = 0, SourceEnd = 1, TargetLabel = label, Prompt = $"Tìm {label}", StorySceneId = sceneId,
            ParentStoryPackageId = packageId, IsDeleted = deleted, HotspotJson = hotspot
        };
        uow.Seed(interaction);
        return interaction;
    }

    [Fact]
    public async Task HotspotBackfill_LocatesTheMissingBox_OnTheCurrentPicture()
    {
        var uow = Seed();
        var picture = SeedPicture(uow);
        var interaction = SeedSearch(uow, 810);
        var locator = new FakeLocator(Found);
        var service = Create(uow, storage: new ReadableStorage(), locator: locator);

        Assert.True(await service.BackfillListeningHotspotsAsync());

        var box = HotspotBox.TryParse(interaction.HotspotJson);
        Assert.NotNull(box);
        Assert.Equal(picture.Id, box!.AssetId);
        Assert.Equal((0.40, 0.50, 0.20, 0.20), (box.X, box.Y, box.W, box.H));
        Assert.Equal(["Thỏ"], locator.Labels);
    }

    [Fact]
    public async Task HotspotBackfill_ProviderFindsNothing_RetriesOnlyAfterTheCooldown()
    {
        var uow = Seed();
        SeedPicture(uow);
        var interaction = SeedSearch(uow, 811);
        var locator = new FakeLocator(null);
        var service = Create(uow, storage: new ReadableStorage(), locator: locator);

        Assert.True(await service.BackfillListeningHotspotsAsync());
        Assert.Null(interaction.HotspotJson);
        Assert.False(await service.BackfillListeningHotspotsAsync());     // chưa hết thời gian chờ: không gọi dồn nhà cung cấp
        Assert.Single(locator.Labels);
    }

    [Fact]
    public async Task HotspotBackfill_ReviewerEditsTheLabel_IsLocatedAgainWithoutWaiting()
    {
        var uow = Seed();
        SeedPicture(uow);
        var interaction = SeedSearch(uow, 812, "Thỏ");
        var locator = new FakeLocator(null);
        var service = Create(uow, storage: new ReadableStorage(), locator: locator);
        Assert.True(await service.BackfillListeningHotspotsAsync());       // lần đầu không thấy "Thỏ"

        interaction.TargetLabel = "bụi cây";                                // người duyệt đổi tên đồ vật (hotspot vẫn null)
        Assert.True(await service.BackfillListeningHotspotsAsync());

        Assert.Equal(["Thỏ", "bụi cây"], locator.Labels);
    }

    [Fact]
    public async Task HotspotBackfill_AfterEditClearedAnOldBox_FindsTheNewOne()
    {
        var uow = Seed();
        var picture = SeedPicture(uow);
        var interaction = SeedSearch(uow, 813, "Thỏ", hotspot: new HotspotBox(picture.Id, 0.1, 0.1, 0.2, 0.2).ToJson());
        var service = Create(uow, storage: new ReadableStorage(), locator: new FakeLocator(Found));
        Assert.False(await service.BackfillListeningHotspotsAsync());       // đã có khung: không việc gì
        interaction.HotspotJson = null;                                      // review đã xóa khung sau khi sửa
        Assert.True(await service.BackfillListeningHotspotsAsync());
        Assert.Equal(0.40, HotspotBox.TryParse(interaction.HotspotJson)!.X);
    }

    [Theory]
    [InlineData(1, "Thỏ", null, false, 600)]      // gói phụ huynh: manifest đã chốt media
    [InlineData(2, "Thỏ", null, true, 600)]       // đã xóa mềm
    [InlineData(3, null, null, false, 600)]       // chưa có tên đồ vật
    [InlineData(4, "Thỏ", null, false, null)]     // chưa gắn scene
    public async Task HotspotBackfill_SkipsIneligibleInteractions(int variant, string? label, int? unused, bool deleted, int? sceneId)
    {
        var uow = Seed();
        SeedPicture(uow);
        var locator = new FakeLocator(Found);
        SeedSearch(uow, 820 + variant, label, packageId: variant == 1 ? 5 : null, deleted: deleted, sceneId: sceneId);
        var service = Create(uow, storage: new ReadableStorage(), locator: locator);

        Assert.False(await service.BackfillListeningHotspotsAsync());

        Assert.Empty(locator.Labels);
    }

    [Fact]
    public async Task HotspotBackfill_NoLocatorConfigured_DoesNothing()
    {
        var uow = Seed();
        SeedPicture(uow);
        var interaction = SeedSearch(uow, 830);
        Assert.False(await Create(uow, storage: new ReadableStorage()).BackfillListeningHotspotsAsync());
        Assert.Null(interaction.HotspotJson);
    }

    [Fact]
    public async Task HotspotBackfill_PictureNotReadyYet_LeavesTheBoxEmpty()
    {
        var uow = Seed();
        var picture = SeedPicture(uow);
        picture.ValidationStatus = ValidationStatus.Pending;
        var interaction = SeedSearch(uow, 831);
        var locator = new FakeLocator(Found);
        await Create(uow, storage: new ReadableStorage(), locator: locator).BackfillListeningHotspotsAsync();
        Assert.Null(interaction.HotspotJson);
        Assert.Empty(locator.Labels);
    }

    [Fact]
    public async Task HotspotBackfill_UnusableBoxFromProvider_IsRejected()
    {
        var uow = Seed();
        SeedPicture(uow);
        var interaction = SeedSearch(uow, 832);
        var service = Create(uow, storage: new ReadableStorage(), locator: new FakeLocator(new LocatedObject(0.9, 0.9, 0.5, 0.5, 0.9)));
        await service.BackfillListeningHotspotsAsync();
        Assert.Null(interaction.HotspotJson);
    }

    [Fact]
    public async Task HotspotBackfill_TrueFalseEvidence_IsLocatedOnThePictureOfTheAnchorScene()
    {
        var uow = Seed();
        var picture = SeedPicture(uow);
        uow.Seed(new ListeningInteraction { Id = 833, StoryVersionId = 10, TargetAge = 7, Type = ListeningInteractionType.TrueFalse,
            AnchorOffset = FirstBlockEnd, StorySceneId = 600 });
        var evidence = new ListeningInteractionOption { Id = 940, ListeningInteractionId = 833, SortOrder = 1, Text = "Thỏ nghe tiếng động",
            IsMatch = true, ImageDescription = "bụi cây" };
        var noEvidence = new ListeningInteractionOption { Id = 941, ListeningInteractionId = 833, SortOrder = 2, Text = "Thỏ ngủ" };
        uow.Seed(evidence);
        uow.Seed(noEvidence);
        var locator = new FakeLocator(Found);
        Assert.True(await Create(uow, storage: new ReadableStorage(), locator: locator).BackfillListeningHotspotsAsync());
        Assert.Equal(["bụi cây"], locator.Labels);
        Assert.Equal(picture.Id, HotspotBox.TryParse(evidence.HotspotJson)!.AssetId);
        Assert.Null(noEvidence.HotspotJson);
    }

    [Fact]
    public async Task RunPackage_LocatesPictureSearchHotspotsOnceTheIllustrationIsReady()
    {
        var uow = Seed();
        var interaction = new ListeningInteraction { Id = 834, StoryVersionId = 10, TargetAge = 7, Type = ListeningInteractionType.PictureSearch,
            AnchorOffset = FirstBlockEnd, SourceStart = 0, SourceEnd = 1, TargetLabel = "A", Prompt = "Tìm A" };
        uow.Seed(interaction);
        var locator = new FakeLocator(Found);
        Assert.True((await Create(uow, storage: new ReadableStorage(), locator: locator).RunPackageAsync()).Success);
        var box = HotspotBox.TryParse(interaction.HotspotJson);
        Assert.NotNull(box);
        Assert.Contains(uow.Items<MediaAsset>(), x => x.Id == box!.AssetId && x.Type == MediaType.Illustration && x.IsCurrent);
        Assert.NotEmpty(locator.Labels);
    }

    // ---- Readiness: tương tác Tìm chi tiết cần khung đồ vật trên tranh hiện tại ----
    private static async Task<IReadOnlyList<string>> MissingMedia(StoryReviewServiceTests.FakeUnitOfWork uow)
    {
        var story = uow.Items<Story>().Single();
        story.Source = StorySource.Ai;
        story.OutputMode = StoryOutputMode.Learning;
        story.AgeBand = "6-8";
        return (await new MediaReadinessService(uow).CheckAsync(10, null)).MissingListeningMedia;
    }

    [Fact]
    public async Task Readiness_PictureSearchWithoutABox_IsNotReady()
    {
        var uow = Seed();
        SeedPicture(uow);
        SeedSearch(uow, 840);
        Assert.Contains("840:i:hotspot", await MissingMedia(uow));
    }

    [Fact]
    public async Task Readiness_PictureSearchWithABoxOnTheCurrentPicture_DoesNotNeedAHotspot()
    {
        var uow = Seed();
        var picture = SeedPicture(uow);
        SeedSearch(uow, 841, hotspot: new HotspotBox(picture.Id, 0.4, 0.5, 0.2, 0.2).ToJson());
        Assert.DoesNotContain("841:i:hotspot", await MissingMedia(uow));
    }

    [Fact]
    public async Task Readiness_BoxOfARetiredPicture_IsNotReady()
    {
        var uow = Seed();
        var picture = SeedPicture(uow);
        SeedSearch(uow, 842, hotspot: new HotspotBox(picture.Id, 0.4, 0.5, 0.2, 0.2).ToJson());
        picture.IsCurrent = false;
        Assert.Contains("842:i:hotspot", await MissingMedia(uow));
    }

    [Fact]
    public async Task Readiness_BoxOfAnotherScenesPicture_IsNotReady()
    {
        var uow = Seed();
        SeedPicture(uow);
        SeedSearch(uow, 843, hotspot: new HotspotBox(9999, 0.4, 0.5, 0.2, 0.2).ToJson());
        Assert.Contains("843:i:hotspot", await MissingMedia(uow));
    }
}
