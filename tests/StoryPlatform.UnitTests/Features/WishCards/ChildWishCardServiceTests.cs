using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.WishCards;
using StoryPlatform.Application.Features.WishCards.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.ReadingSessions;
using Xunit;

namespace StoryPlatform.UnitTests.Features.WishCards;

public class ChildWishCardServiceTests
{
    private const int Violence = 7;
    private const int Animals = 1;

    private readonly ReadingHarness _h = new();
    private readonly ChildWishCardService _sut;

    public ChildWishCardServiceTests()
    {
        _sut = new ChildWishCardService(_h.UnitOfWork.Object);
        _h.AddCategory(Animals, "ANIMALS");
        _h.AddCategory(Violence, "VIOLENCE");
    }

    private Task<IReadOnlyList<ChildWishCardDto>> List(string group = "Character") =>
        _sut.ListAsync(ReadingHarness.Child, group);

    private SafetyPolicy Policy(bool wishesEnabled = true)
    {
        var policy = new SafetyPolicy
        {
            Id = _h.Policies.Count + 1, ChildProfileId = ReadingHarness.Child, StoryWishesEnabled = wishesEnabled
        };
        _h.Policies.Add(policy);
        return policy;
    }

    private void Rule(SafetyPolicy policy, int categoryId, PolicyRule rule) =>
        _h.PolicyCategories.Add(new SafetyPolicyCategory
        {
            Id = _h.PolicyCategories.Count + 1, SafetyPolicyId = policy.Id, ContentCategoryId = categoryId, Rule = rule
        });

    [Fact]
    public async Task ListAsync_ReturnsTheGroupsActiveCardsInSortOrder_WithOnlyChildFacingFields()
    {
        _h.AddCard(WishCardGroup.Character, "Chú rồng", sortOrder: 2);
        _h.AddCard(WishCardGroup.Character, "Chú mèo", sortOrder: 1);
        _h.AddCard(WishCardGroup.Place, "Khu rừng");

        var cards = await List("character");

        Assert.Equal(new[] { "Chú mèo", "Chú rồng" }, cards.Select(c => c.Label));
        Assert.All(cards, c => Assert.Equal("Character", c.Group));
        Assert.All(cards, c => Assert.StartsWith("https://", c.ImageUrl));
        Assert.Null(typeof(ChildWishCardDto).GetProperty("IsActive"));
        Assert.Null(typeof(ChildWishCardDto).GetProperty("ContentCategoryId"));
    }

    [Fact]
    public async Task ListAsync_HiddenCardsNeverAppear()
    {
        _h.AddCard(WishCardGroup.Character, "Chú mèo");
        _h.AddCard(WishCardGroup.Character, "Chú chó", isActive: false);

        Assert.Equal(new[] { "Chú mèo" }, (await List()).Select(c => c.Label));
    }

    [Fact]
    public async Task ListAsync_OnlyCardsForTheChildsAgeOrForEveryAge()
    {
        _h.AddCard(WishCardGroup.Character, "Mọi tuổi", sortOrder: 1);
        _h.AddCard(WishCardGroup.Character, "Cho 6-8", sortOrder: 2, ageBand: AgeBand.Age_6_8);
        _h.AddCard(WishCardGroup.Character, "Cho 9-10", sortOrder: 3, ageBand: AgeBand.Age_9_10);

        Assert.Equal(new[] { "Mọi tuổi", "Cho 6-8" }, (await List()).Select(c => c.Label));
    }

    [Fact]
    public async Task ListAsync_BlockedLabelNeverAppears_WhileOtherLabelsDo()
    {
        _h.AddCard(WishCardGroup.Challenge, "Đánh nhau", categoryId: Violence);
        _h.AddCard(WishCardGroup.Challenge, "Giúp bạn", categoryId: Animals);
        _h.AddCard(WishCardGroup.Challenge, "Bị lạc đường");
        Rule(Policy(), Violence, PolicyRule.Blocked);

        var cards = await List("Challenge");

        Assert.DoesNotContain(cards, c => c.Label == "Đánh nhau");
        Assert.Equal(new[] { "Giúp bạn", "Bị lạc đường" }.OrderBy(x => x), cards.Select(c => c.Label).OrderBy(x => x));
    }

    [Fact]
    public async Task ListAsync_WhenThereIsAnAllowedList_OnlyAllowedLabelsAppear_AndUnlabelledCardsAreHidden()
    {
        _h.AddCard(WishCardGroup.Character, "Con vật", categoryId: Animals);
        _h.AddCard(WishCardGroup.Character, "Không nhãn");
        _h.AddCard(WishCardGroup.Character, "Bạo lực", categoryId: Violence);
        Rule(Policy(), Animals, PolicyRule.Allowed);

        Assert.Equal(new[] { "Con vật" }, (await List()).Select(c => c.Label));
    }

    [Fact]
    public async Task ListAsync_NoPolicyAtAll_MeansNothingIsBlockedAndWishesAreOn()
    {
        _h.AddCard(WishCardGroup.Character, "Chú mèo", categoryId: Violence);

        Assert.Single(await List());
    }

    [Fact]
    public async Task ListAsync_ReturnsAtMostEightCards_TheFirstEightInOrder()
    {
        for (var i = 1; i <= 10; i++)
        {
            _h.AddCard(WishCardGroup.Character, $"Thẻ {i:00}", sortOrder: i);
        }

        var cards = await List();

        Assert.Equal(WishCardRules.MaxCardsPerWishScreen, cards.Count);
        Assert.Equal("Thẻ 01", cards[0].Label);
        Assert.Equal("Thẻ 08", cards[^1].Label);
    }

    [Fact]
    public async Task ListAsync_WishesTurnedOff_IsForbiddenWithFriendlyMessageAndNoCards()
    {
        _h.AddCard(WishCardGroup.Character, "Chú mèo");
        Policy(wishesEnabled: false);

        var error = await Assert.ThrowsAsync<ForbiddenException>(() => List());

        Assert.Equal(ChildWishCardPolicy.WishesOffMessage, error.Message);
        Assert.DoesNotContain("tắt", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Desire")]
    [InlineData("Event")]
    [InlineData("Ending")]
    [InlineData("Animal")]
    [InlineData("")]
    [InlineData(null)]
    public async Task ListAsync_NotAWishGroup_IsBadRequest(string? group)
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ListAsync(ReadingHarness.Child, group));
    }

    [Theory]
    [InlineData(ChildProfileStatus.Suspended)]
    [InlineData(ChildProfileStatus.Archived)]
    public async Task ListAsync_ChildNotActive_IsForbidden(ChildProfileStatus status)
    {
        _h.Children.Single(c => c.Id == ReadingHarness.Child).Status = status;

        await Assert.ThrowsAsync<ForbiddenException>(() => List());
    }

    [Fact]
    public async Task ListAsync_UnknownChild_IsForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ListAsync(999, "Character"));
    }
}
