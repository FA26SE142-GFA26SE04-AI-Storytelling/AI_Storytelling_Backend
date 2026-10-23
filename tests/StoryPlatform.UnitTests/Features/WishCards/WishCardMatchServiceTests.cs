using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.WishCards.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.ReadingSessions;
using Xunit;

namespace StoryPlatform.UnitTests.Features.WishCards;

public class WishCardMatchServiceTests
{
    private const int Moderator = 11;

    private readonly ReadingHarness _h = new();
    private readonly Mock<IAuditLogWriter> _audit = new();
    private readonly WishCardMatchService _sut;
    private readonly WishCard _cat;
    private readonly WishCard _forest;

    public WishCardMatchServiceTests()
    {
        _cat = _h.AddCard(WishCardGroup.Character, "Chú mèo");
        _forest = _h.AddCard(WishCardGroup.Place, "Khu rừng");
        _h.AddLibraryStory(20, "Mèo con lạc trong rừng");
        _sut = new WishCardMatchService(_h.UnitOfWork.Object, _audit.Object);
    }

    [Fact]
    public async Task Add_StoresAModeratorMatch_AndAudits()
    {
        var match = await _sut.AddAsync(Moderator, 20, _cat.Id);

        var stored = Assert.Single(_h.CardMatches);
        Assert.Equal(WishCardMatchSource.Moderator, stored.Source);
        Assert.Equal(Moderator, stored.CreatedByUserId);
        Assert.Equal("Moderator", match.Source);
        Assert.Equal("Chú mèo", match.Label);
        Assert.Equal("Character", match.Group);
        _audit.Verify(a => a.LogAsync(Moderator, "WishCardMatchAdded", nameof(StoryWishCardMatch), stored.Id,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Add_Twice_KeepsOneRow()
    {
        await _sut.AddAsync(Moderator, 20, _cat.Id);
        await _sut.AddAsync(Moderator, 20, _cat.Id);

        Assert.Single(_h.CardMatches);
    }

    [Fact]
    public async Task Add_ACreationGroupCard_IsBadRequest()
    {
        var ending = _h.AddCard(WishCardGroup.Ending, "Vui vẻ cùng bạn bè");

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.AddAsync(Moderator, 20, ending.Id));
        Assert.Empty(_h.CardMatches);
    }

    [Fact]
    public async Task Add_UnknownStoryOrCard_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.AddAsync(Moderator, 404, _cat.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.AddAsync(Moderator, 20, 404));
    }

    [Fact]
    public async Task List_ShowsTheStorysCardsInGroupOrder_WithSource()
    {
        _h.CardMatches.Add(new StoryWishCardMatch { Id = 1, StoryId = 20, WishCardId = _forest.Id, Source = WishCardMatchSource.Ai });
        _h.CardMatches.Add(new StoryWishCardMatch { Id = 2, StoryId = 20, WishCardId = _cat.Id, Source = WishCardMatchSource.Moderator });
        _h.CardMatches.Add(new StoryWishCardMatch { Id = 3, StoryId = 21, WishCardId = _cat.Id, Source = WishCardMatchSource.Moderator });

        var list = await _sut.ListAsync(20);

        Assert.Equal(new[] { "Chú mèo", "Khu rừng" }, list.Select(m => m.Label));
        Assert.Equal(new[] { "Moderator", "Ai" }, list.Select(m => m.Source));
    }

    [Fact]
    public async Task List_UnknownStory_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ListAsync(404));
    }

    [Fact]
    public async Task Remove_DeletesTheRow_AndAudits_AndUnknownIsNotFound()
    {
        await _sut.AddAsync(Moderator, 20, _cat.Id);

        await _sut.RemoveAsync(Moderator, 20, _cat.Id);

        Assert.Empty(_h.CardMatches);
        _audit.Verify(a => a.LogAsync(Moderator, "WishCardMatchRemoved", nameof(StoryWishCardMatch), It.IsAny<int>(),
            It.IsAny<object?>(), null, It.IsAny<CancellationToken>()), Times.Once);
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.RemoveAsync(Moderator, 20, _cat.Id));
    }
}
