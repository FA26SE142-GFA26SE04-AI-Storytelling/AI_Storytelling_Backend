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
    private readonly ContentCategoryService _sut;

    public ContentCategoryServiceTests()
    {
        _unitOfWork.Setup(work => work.Repository<ContentCategory>()).Returns(_repository.Object);
        _sut = new ContentCategoryService(_unitOfWork.Object);
    }

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
        Assert.Equal(9, added.CreatedByAdminId);
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

    [Fact]
    public async Task GetByIdAsync_NotFound_ThrowsNotFound()
    {
        _repository.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentCategory?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetByIdAsync(1));
    }

    [Fact]
    public async Task ListAsync_ReturnsAllMappedCategories()
    {
        _repository.Setup(repository => repository.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContentCategory>
            {
                new() { Id = 1, Code = "animals", DisplayName = "Động vật", IsActive = true },
                new() { Id = 2, Code = "space", DisplayName = "Vũ trụ", IsActive = false }
            });

        var result = await _sut.ListAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("animals", result[0].Code);
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
        _repository.Setup(repository => repository.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContentCategory>
            {
                new() { Id = 7, Code = "VIOLENCE", DisplayName = "Bạo lực", Group = ContentCategoryGroup.SafetyWarning }
            });

        var result = await _sut.ListAsync();

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

        await sut.ListAsync();
        var second = await sut.ListAsync();

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

        await sut.GetByIdAsync(5);
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

        await Assert.ThrowsAsync<NotFoundException>(() => sut.GetByIdAsync(42));

        Assert.False(cache.Contains(ContentCategoryService.ItemCacheKey(42)));
    }
}
