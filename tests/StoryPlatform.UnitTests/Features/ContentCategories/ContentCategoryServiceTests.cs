using StoryPlatform.UnitTests.Infrastructure.Caching;
using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ContentCategories.DTOs;
using StoryPlatform.Application.Features.ContentCategories.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ContentCategories;

public class ContentCategoryServiceTests
{
    private readonly Mock<IGenericRepository<ContentCategory>> _repository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly List<StoryCategory> _storyCategories = new();
    private readonly List<SafetyPolicyCategory> _policyCategories = new();
    private readonly List<WishCard> _wishCards = new();
    private readonly List<ReadingRoadmap> _roadmaps = new();
    private readonly List<ModerationItem> _moderationItems = new();
    private readonly ContentCategoryService _sut;

    public ContentCategoryServiceTests()
    {
        _unitOfWork.Setup(work => work.Repository<ContentCategory>()).Returns(_repository.Object);
        WireInMemory(_storyCategories);
        WireInMemory(_policyCategories);
        WireInMemory(_wishCards);
        WireInMemory(_roadmaps);
        WireInMemory(_moderationItems);
        _sut = new ContentCategoryService(_unitOfWork.Object);
    }

    private void WireInMemory<T>(List<T> rows) where T : class
    {
        var repository = new Mock<IGenericRepository<T>>();
        repository.Setup(repo => repo.ExistsAsync(It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, CancellationToken _) => rows.Any(predicate.Compile()));
        repository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, string? _, CancellationToken _) =>
                (IReadOnlyList<T>)rows.Where(predicate.Compile()).ToList());
        _unitOfWork.Setup(work => work.Repository<T>()).Returns(repository.Object);
    }

    private void SetupCategoryFind(List<ContentCategory> rows) =>
        _repository.Setup(repository => repository.FindAsync(
                It.IsAny<Expression<Func<ContentCategory, bool>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<ContentCategory, bool>> predicate, string? _, CancellationToken _) =>
                (IReadOnlyList<ContentCategory>)rows.Where(predicate.Compile()).ToList());

    [Fact]
    public async Task CreateAsync_DuplicateCode_ThrowsConflict()
    {
        _repository.Setup(repository => repository.ExistsAsync(
                It.IsAny<Expression<Func<ContentCategory, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.CreateAsync(
            9, new CreateContentCategoryRequestDto { Code = "animals", DisplayName = "Động vật" }));

        _repository.Verify(repository => repository.AddAsync(
            It.IsAny<ContentCategory>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_NewCode_CreatesAndSaves()
    {
        _repository.Setup(repository => repository.ExistsAsync(
                It.IsAny<Expression<Func<ContentCategory, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        ContentCategory? added = null;
        _repository.Setup(repository => repository.AddAsync(
                It.IsAny<ContentCategory>(), It.IsAny<CancellationToken>()))
            .Callback<ContentCategory, CancellationToken>((category, _) => added = category)
            .ReturnsAsync((ContentCategory category, CancellationToken _) => category);

        var result = await _sut.CreateAsync(
            9, new CreateContentCategoryRequestDto { Code = " animals ", DisplayName = " Động vật ", Group = " lifevalue " });

        Assert.Equal("animals", added!.Code);
        Assert.Equal(ContentCategoryGroup.LifeValue, added.Group);
        Assert.Equal("LifeValue", result.Group);
        Assert.Equal("Động vật", added.DisplayName);
        Assert.Equal(9, added.CreatedByModeratorId);
        Assert.Equal("animals", result.Code);
        _unitOfWork.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_NotFound_ThrowsNotFound()
    {
        _repository.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentCategory?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.UpdateAsync(
            1, new UpdateContentCategoryRequestDto { DisplayName = "X" }));
    }

    [Fact]
    public async Task UpdateAsync_Exists_UpdatesAndSaves()
    {
        var category = new ContentCategory { Id = 1, Code = "animals", DisplayName = "Cũ", IsActive = true };
        _repository.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        var result = await _sut.UpdateAsync(
            1, new UpdateContentCategoryRequestDto { DisplayName = "Động vật", IsActive = false });

        Assert.Equal("Động vật", category.DisplayName);
        Assert.False(category.IsActive);
        Assert.Equal("animals", result.Code);
        _repository.Verify(repository => repository.Update(category), Times.Once);
        _unitOfWork.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_Exists_DeletesAndSaves()
    {
        var category = new ContentCategory { Id = 1, Code = "animals" };
        _repository.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        await _sut.DeleteAsync(1);

        _repository.Verify(repository => repository.Delete(category), Times.Once);
        _unitOfWork.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("story", "truyện")]
    [InlineData("policy", "chính sách an toàn")]
    [InlineData("wish", "thẻ điều ước")]
    [InlineData("roadmap", "lộ trình đọc")]
    [InlineData("proposal", "hồ sơ gửi duyệt")]
    public async Task DeleteAsync_InUse_ThrowsConflictWithoutDeleting(string source, string expectedUsage)
    {
        var category = new ContentCategory { Id = 1, Code = "animals" };
        _repository.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);
        switch (source)
        {
            case "story": _storyCategories.Add(new StoryCategory { StoryId = 3, ContentCategoryId = 1 }); break;
            case "policy": _policyCategories.Add(new SafetyPolicyCategory { SafetyPolicyId = 2, ContentCategoryId = 1 }); break;
            case "wish": _wishCards.Add(new WishCard { Id = 4, ContentCategoryId = 1 }); break;
            case "roadmap": _roadmaps.Add(new ReadingRoadmap { Id = 5, MainCategoryId = 1 }); break;
            case "proposal": _moderationItems.Add(new ModerationItem { Id = 6, ProposedContentCategoryIds = "[1,7]" }); break;
        }

        var error = await Assert.ThrowsAsync<ConflictException>(() => _sut.DeleteAsync(1));

        Assert.Contains(expectedUsage, error.Message);
        Assert.Contains("IsActive = false", error.Message);
        _repository.Verify(repository => repository.Delete(It.IsAny<ContentCategory>()), Times.Never);
        _unitOfWork.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_OnlyOtherCategoriesReferenced_Deletes()
    {
        var category = new ContentCategory { Id = 1, Code = "animals" };
        _repository.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);
        _storyCategories.Add(new StoryCategory { StoryId = 3, ContentCategoryId = 2 });
        _wishCards.Add(new WishCard { Id = 4, ContentCategoryId = null });
        _roadmaps.Add(new ReadingRoadmap { Id = 5, MainCategoryId = 2 });
        _moderationItems.Add(new ModerationItem { Id = 6, ProposedContentCategoryIds = "[11,21]" });
        _moderationItems.Add(new ModerationItem { Id = 7, ProposedContentCategoryIds = "not-json" });

        await _sut.DeleteAsync(1);

        _repository.Verify(repository => repository.Delete(category), Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ThrowsNotFound()
    {
        _repository.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentCategory?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetByIdAsync(1, includeInactive: true));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task GetByIdAsync_Inactive_VisibleOnlyWhenIncluded(bool includeInactive, bool visible)
    {
        _repository.Setup(repository => repository.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentCategory { Id = 2, Code = "space", IsActive = false });

        if (visible)
        {
            Assert.Equal("space", (await _sut.GetByIdAsync(2, includeInactive)).Code);
        }
        else
        {
            await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetByIdAsync(2, includeInactive));
        }
    }

    [Fact]
    public async Task ListAsync_IncludeInactive_ReturnsAllMappedCategories()
    {
        _repository.Setup(repository => repository.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContentCategory>
            {
                new() { Id = 1, Code = "animals", DisplayName = "Động vật", IsActive = true },
                new() { Id = 2, Code = "space", DisplayName = "Vũ trụ", IsActive = false }
            });

        var result = await _sut.ListAsync(includeInactive: true);

        Assert.Equal(2, result.Count);
        Assert.Equal("animals", result[0].Code);
    }

    [Fact]
    public async Task ListAsync_Default_ReturnsActiveOnly()
    {
        SetupCategoryFind(new List<ContentCategory>
        {
            new() { Id = 1, Code = "animals", DisplayName = "Động vật", IsActive = true },
            new() { Id = 2, Code = "space", DisplayName = "Vũ trụ", IsActive = false }
        });

        var result = await _sut.ListAsync(includeInactive: false);

        Assert.Equal("animals", Assert.Single(result).Code);
        _repository.Verify(repository => repository.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithDefaultBlockedAgeBands_NormalizesAndStores()
    {
        _repository.Setup(repository => repository.ExistsAsync(
                It.IsAny<Expression<Func<ContentCategory, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        ContentCategory? added = null;
        _repository.Setup(repository => repository.AddAsync(
                It.IsAny<ContentCategory>(), It.IsAny<CancellationToken>()))
            .Callback<ContentCategory, CancellationToken>((category, _) => added = category)
            .ReturnsAsync((ContentCategory category, CancellationToken _) => category);

        var result = await _sut.CreateAsync(9, new CreateContentCategoryRequestDto
        {
            Code = "horror",
            DisplayName = "Kinh dị",
            Group = "SafetyWarning",
            DefaultBlockedAgeBands = new List<string> { "Age_6_8", " Age_6_8 " }
        });

        Assert.Equal(new[] { "Age_6_8" }, added!.DefaultBlockedAgeBands);
        Assert.Equal(new[] { "Age_6_8" }, result.DefaultBlockedAgeBands);
    }

    [Fact]
    public async Task CreateAsync_UnknownAgeBand_ThrowsBadRequest()
    {
        _repository.Setup(repository => repository.ExistsAsync(
                It.IsAny<Expression<Func<ContentCategory, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateAsync(9,
            new CreateContentCategoryRequestDto
            {
                Code = "x",
                DisplayName = "X",
                Group = "Genre",
                DefaultBlockedAgeBands = new List<string> { "Age_99" }
            }));
    }

    [Fact]
    public async Task UpdateAsync_NullAgeBands_KeepsExistingAndListReplacesThem()
    {
        var category = new ContentCategory { Id = 4, Code = "horror", DisplayName = "Kinh dị" };
        category.DefaultBlockedAgeBands.Add("Age_6_8");
        _repository.Setup(repository => repository.GetByIdAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        await _sut.UpdateAsync(4, new UpdateContentCategoryRequestDto { DisplayName = "Kinh dị" });
        Assert.Equal(new[] { "Age_6_8" }, category.DefaultBlockedAgeBands);

        await _sut.UpdateAsync(4, new UpdateContentCategoryRequestDto
        {
            DisplayName = "Kinh dị",
            DefaultBlockedAgeBands = new List<string> { "Age_9_10" }
        });
        Assert.Equal(new[] { "Age_9_10" }, category.DefaultBlockedAgeBands);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Unknown")]
    [InlineData("4")]
    public async Task CreateAsync_MissingOrInvalidGroup_ThrowsBadRequest(string? group)
    {
        _repository.Setup(repository => repository.ExistsAsync(
                It.IsAny<Expression<Func<ContentCategory, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var error = await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateAsync(9,
            new CreateContentCategoryRequestDto { Code = "x", DisplayName = "X", Group = group }));

        Assert.Equal("Nhóm nhãn không hợp lệ.", error.Message);
        _repository.Verify(repository => repository.AddAsync(
            It.IsAny<ContentCategory>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_NullGroup_KeepsExistingAndValueReplacesIt()
    {
        var category = new ContentCategory
        {
            Id = 5, Code = "friendship", DisplayName = "Tình bạn", Group = ContentCategoryGroup.LifeValue
        };
        _repository.Setup(repository => repository.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        await _sut.UpdateAsync(5, new UpdateContentCategoryRequestDto { DisplayName = "Tình bạn" });
        Assert.Equal(ContentCategoryGroup.LifeValue, category.Group);

        var result = await _sut.UpdateAsync(5,
            new UpdateContentCategoryRequestDto { DisplayName = "Tình bạn", Group = "Genre" });
        Assert.Equal(ContentCategoryGroup.Genre, category.Group);
        Assert.Equal("Genre", result.Group);
    }

    [Fact]
    public async Task UpdateAsync_InvalidGroup_ThrowsBadRequestWithoutSaving()
    {
        var category = new ContentCategory { Id = 5, Code = "friendship", DisplayName = "Tình bạn" };
        _repository.Setup(repository => repository.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.UpdateAsync(5,
            new UpdateContentCategoryRequestDto { DisplayName = "Tình bạn", Group = "Other" }));

        _unitOfWork.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListAsync_MapsGroup()
    {
        SetupCategoryFind(new List<ContentCategory>
        {
            new() { Id = 7, Code = "VIOLENCE", DisplayName = "Bạo lực", Group = ContentCategoryGroup.SafetyWarning }
        });

        var result = await _sut.ListAsync(includeInactive: false);

        Assert.Equal("SafetyWarning", Assert.Single(result).Group);
    }

    // ---------- Cache ----------

    [Fact]
    public async Task ListAsync_WithCache_SecondCallSkipsDatabase()
    {
        var cache = new InMemoryAppCache();
        var sut = new ContentCategoryService(_unitOfWork.Object, cache);
        _repository.Setup(repository => repository.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContentCategory> { new() { Id = 1, Code = "animals", DisplayName = "Động vật" } });

        await sut.ListAsync(includeInactive: true);
        var second = await sut.ListAsync(includeInactive: true);

        Assert.Single(second);
        _repository.Verify(repository => repository.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_InvalidatesListAndItemCache()
    {
        var cache = new InMemoryAppCache();
        var sut = new ContentCategoryService(_unitOfWork.Object, cache);
        _repository.Setup(repository => repository.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentCategory { Id = 5, Code = "animals", DisplayName = "Cũ" });

        await sut.GetByIdAsync(5, includeInactive: true);
        await sut.UpdateAsync(5, new UpdateContentCategoryRequestDto { DisplayName = "Mới", IsActive = true });

        Assert.Contains(ContentCategoryService.ListCacheKey, cache.RemovedKeys);
        Assert.Contains(ContentCategoryService.ItemCacheKey(5), cache.RemovedKeys);
        Assert.False(cache.Contains(ContentCategoryService.ItemCacheKey(5)));
    }

    [Fact]
    public async Task CreateAndDeleteAsync_InvalidateListCache()
    {
        var cache = new InMemoryAppCache();
        var sut = new ContentCategoryService(_unitOfWork.Object, cache);
        _repository.Setup(repository => repository.ExistsAsync(
                It.IsAny<Expression<Func<ContentCategory, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _repository.Setup(repository => repository.GetByIdAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentCategory { Id = 3, Code = "x", DisplayName = "x" });

        await sut.CreateAsync(9, new CreateContentCategoryRequestDto { Code = "sea", DisplayName = "Biển", Group = "Genre" });
        await sut.DeleteAsync(3);

        Assert.Equal(2, cache.RemovedKeys.Count(key => key == ContentCategoryService.ListCacheKey));
        Assert.Contains(ContentCategoryService.ItemCacheKey(3), cache.RemovedKeys);
    }

    [Fact]
    public async Task GetByIdAsync_Missing_ThrowsNotFoundAndCachesNothing()
    {
        var cache = new InMemoryAppCache();
        var sut = new ContentCategoryService(_unitOfWork.Object, cache);

        await Assert.ThrowsAsync<NotFoundException>(() => sut.GetByIdAsync(42, includeInactive: true));

        Assert.False(cache.Contains(ContentCategoryService.ItemCacheKey(42)));
    }
}
