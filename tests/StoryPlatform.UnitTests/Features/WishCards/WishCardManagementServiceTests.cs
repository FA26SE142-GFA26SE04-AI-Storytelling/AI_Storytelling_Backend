using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.WishCards;
using StoryPlatform.Application.Features.WishCards.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.ReadingSessions;
using Xunit;

namespace StoryPlatform.UnitTests.Features.WishCards;

public class WishCardManagementServiceTests
{
    private const int Moderator = 11;

    private readonly ReadingHarness _h = new();
    private readonly Mock<IAuditLogWriter> _audit = new();
    private readonly WishCardManagementService _sut;

    public WishCardManagementServiceTests()
    {
        _sut = new WishCardManagementService(_h.UnitOfWork.Object, _audit.Object);
    }

    private static CreateWishCardRequestDto Create(
        string group = "Character", string label = "Chú mèo", string image = "https://cdn.test/cat.png") =>
        new() { Group = group, Label = label, ImageUrl = image };

    // ---- CreateAsync ----

    [Fact]
    public async Task CreateAsync_StoresTheCardTrimmedAndActive_AndWritesAnAuditEntry()
    {
        _h.AddCategory(7, "VIOLENCE");

        var card = await _sut.CreateAsync(Moderator, new CreateWishCardRequestDto
        {
            Group = "place", Label = "  Khu rừng  ", ImageUrl = "https://cdn.test/forest.png",
            AudioUrl = "https://cdn.test/forest.mp3", AgeBand = "6-8", ContentCategoryId = 7, SortOrder = 3
        });

        var stored = Assert.Single(_h.WishCards);
        Assert.Equal(WishCardGroup.Place, stored.Group);
        Assert.Equal("Khu rừng", stored.Label);
        Assert.Equal("https://cdn.test/forest.mp3", stored.AudioUrl);
        Assert.Equal(AgeBand.Age_6_8, stored.AgeBand);
        Assert.Equal(7, stored.ContentCategoryId);
        Assert.Equal(3, stored.SortOrder);
        Assert.True(stored.IsActive);
        Assert.Equal(Moderator, stored.CreatedByUserId);
        Assert.Equal(stored.Id, card.Id);
        Assert.Equal("Place", card.Group);
        Assert.Equal("6-8", card.AgeBand);
        _audit.Verify(a => a.LogAsync(Moderator, "WishCardCreated", nameof(WishCard), stored.Id,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_NoAgeBandMeansEveryAge()
    {
        var card = await _sut.CreateAsync(Moderator, Create());

        Assert.Null(card.AgeBand);
        Assert.Null(_h.WishCards.Single().AgeBand);
    }

    [Theory]
    [InlineData("", "https://cdn.test/a.png")]
    [InlineData("   ", "https://cdn.test/a.png")]
    public async Task CreateAsync_BlankLabel_IsBadRequest(string label, string image)
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateAsync(Moderator, Create(label: label, image: image)));
        Assert.Empty(_h.WishCards);
    }

    [Fact]
    public async Task CreateAsync_TooLongLabel_IsBadRequest()
    {
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.CreateAsync(Moderator, Create(label: new string('a', WishCardRules.MaxLabelLength + 1))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    [InlineData("khong-phai-url")]
    public async Task CreateAsync_BadImageUrl_IsBadRequest(string image)
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateAsync(Moderator, Create(image: image)));
    }

    [Fact]
    public async Task CreateAsync_BadAudioUrl_IsBadRequest_ButBlankAudioIsAllowed()
    {
        var request = Create();
        request.AudioUrl = "ftp://x/y.mp3";
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateAsync(Moderator, request));

        request.AudioUrl = "   ";
        var card = await _sut.CreateAsync(Moderator, request);
        Assert.Null(card.AudioUrl);
    }

    [Theory]
    [InlineData("Animal")]
    [InlineData("")]
    [InlineData("99")]
    public async Task CreateAsync_UnknownGroup_IsBadRequest(string group)
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateAsync(Moderator, Create(group: group)));
    }

    [Fact]
    public async Task CreateAsync_UnknownAgeBand_IsBadRequest()
    {
        var request = Create();
        request.AgeBand = "11-12";

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateAsync(Moderator, request));
    }

    [Fact]
    public async Task CreateAsync_UnknownOrInactiveCategory_IsBadRequest()
    {
        _h.AddCategory(8, "OLD", isActive: false);
        var request = Create();

        request.ContentCategoryId = 999;
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateAsync(Moderator, request));

        request.ContentCategoryId = 8;
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateAsync(Moderator, request));
    }

    [Fact]
    public async Task CreateAsync_SameLabelInSameGroup_IsConflict_EvenIfHidden_ButOtherGroupIsFine()
    {
        _h.AddCard(WishCardGroup.Character, "Chú mèo", isActive: false);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.CreateAsync(Moderator, Create(label: "chú MÈO")));
        var otherGroup = await _sut.CreateAsync(Moderator, Create(group: "Place", label: "Chú mèo"));

        Assert.Equal("Place", otherGroup.Group);
    }

    // ---- UpdateAsync ----

    [Fact]
    public async Task UpdateAsync_ChangesFields_KeepsGroupAndActiveState_AndAuditsBeforeAndAfter()
    {
        var card = _h.AddCard(WishCardGroup.Character, "Chú mèo", isActive: false);

        var updated = await _sut.UpdateAsync(Moderator, card.Id, new UpdateWishCardRequestDto
        {
            Label = "Chú mèo con", ImageUrl = "https://cdn.test/new.png", AgeBand = "9-10", SortOrder = 5
        });

        Assert.Equal("Chú mèo con", card.Label);
        Assert.Equal(AgeBand.Age_9_10, card.AgeBand);
        Assert.Equal(5, card.SortOrder);
        Assert.Equal(WishCardGroup.Character, card.Group);
        Assert.False(card.IsActive);
        Assert.NotNull(card.UpdatedAt);
        Assert.Equal("9-10", updated.AgeBand);
        _audit.Verify(a => a.LogAsync(Moderator, "WishCardUpdated", nameof(WishCard), card.Id,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_RenamingToAnotherCardsLabel_IsConflict_ButKeepingOwnLabelIsFine()
    {
        var first = _h.AddCard(WishCardGroup.Character, "Chú mèo");
        _h.AddCard(WishCardGroup.Character, "Chú chó");

        await Assert.ThrowsAsync<ConflictException>(() => _sut.UpdateAsync(Moderator, first.Id,
            new UpdateWishCardRequestDto { Label = "Chú chó", ImageUrl = first.ImageUrl }));
        var same = await _sut.UpdateAsync(Moderator, first.Id,
            new UpdateWishCardRequestDto { Label = "Chú mèo", ImageUrl = first.ImageUrl, SortOrder = 9 });

        Assert.Equal(9, same.SortOrder);
    }

    [Fact]
    public async Task UpdateAsync_InvalidData_ChangesNothing()
    {
        var card = _h.AddCard(WishCardGroup.Character, "Chú mèo");

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.UpdateAsync(Moderator, card.Id,
            new UpdateWishCardRequestDto { Label = "Mới", ImageUrl = "khong-phai-url" }));

        Assert.Equal("Chú mèo", card.Label);
    }

    [Fact]
    public async Task UpdateAsync_UnknownCard_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.UpdateAsync(Moderator, 404,
            new UpdateWishCardRequestDto { Label = "x", ImageUrl = "https://cdn.test/x.png" }));
    }

    // ---- SetActiveAsync ----

    [Fact]
    public async Task SetActiveAsync_HidesAndShowsWithoutDeleting_AndAudits()
    {
        var card = _h.AddCard(WishCardGroup.Character, "Chú mèo");

        var hidden = await _sut.SetActiveAsync(Moderator, card.Id, false);
        Assert.False(hidden.IsActive);
        Assert.False(card.IsActive);
        Assert.False(card.IsDeleted);
        Assert.Single(_h.WishCards);

        var shown = await _sut.SetActiveAsync(Moderator, card.Id, true);
        Assert.True(shown.IsActive);
        _audit.Verify(a => a.LogAsync(Moderator, "WishCardVisibilityChanged", nameof(WishCard), card.Id,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SetActiveAsync_NoChange_WritesNoAuditEntry()
    {
        var card = _h.AddCard(WishCardGroup.Character, "Chú mèo");

        await _sut.SetActiveAsync(Moderator, card.Id, true);

        _audit.Verify(a => a.LogAsync(It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetActiveAsync_UnknownCard_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.SetActiveAsync(Moderator, 404, false));
    }

    // ---- ListAsync / GetAsync ----

    [Fact]
    public async Task ListAsync_ShowsHiddenCardsToo_OrderedByGroupThenSortOrder_AndFiltersByGroupAndActive()
    {
        _h.AddCard(WishCardGroup.Place, "Khu rừng", sortOrder: 2);
        _h.AddCard(WishCardGroup.Character, "Chú mèo", sortOrder: 2);
        _h.AddCard(WishCardGroup.Character, "Chú chó", sortOrder: 1, isActive: false);

        var all = await _sut.ListAsync(new WishCardQueryDto());
        var characters = await _sut.ListAsync(new WishCardQueryDto { Group = "Character" });
        var hiddenOnly = await _sut.ListAsync(new WishCardQueryDto { IsActive = false });

        Assert.Equal(new[] { "Chú chó", "Chú mèo", "Khu rừng" }, all.Items.Select(c => c.Label));
        Assert.Equal(new[] { "Chú chó", "Chú mèo" }, characters.Items.Select(c => c.Label));
        Assert.Equal(new[] { "Chú chó" }, hiddenOnly.Items.Select(c => c.Label));
        Assert.Equal(3, all.TotalCount);
    }

    [Fact]
    public async Task ListAsync_UnknownGroupFilter_IsBadRequest()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ListAsync(new WishCardQueryDto { Group = "Animal" }));
    }

    [Fact]
    public async Task ListAsync_Paging_ClampsSizeAndPageIndex()
    {
        for (var i = 0; i < 5; i++)
        {
            _h.AddCard(WishCardGroup.Character, $"Thẻ {i}", sortOrder: i);
        }

        var page = await _sut.ListAsync(new WishCardQueryDto { PageIndex = 2, PageSize = 2 });
        var clamped = await _sut.ListAsync(new WishCardQueryDto { PageIndex = 0, PageSize = 9999 });

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(1, clamped.PageIndex);
        Assert.Equal(100, clamped.PageSize);
    }

    [Fact]
    public async Task GetAsync_ReturnsTheCard_AndUnknownIsNotFound()
    {
        var card = _h.AddCard(WishCardGroup.Lesson, "Chia sẻ");

        Assert.Equal("Chia sẻ", (await _sut.GetAsync(card.Id)).Label);
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetAsync(404));
    }
}
