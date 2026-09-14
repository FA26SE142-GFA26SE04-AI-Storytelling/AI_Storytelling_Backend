using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ContentCategories.DTOs;
using StoryPlatform.Application.Features.ContentCategories.Services;
using StoryPlatform.Domain.Entities;
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
            9, new CreateContentCategoryRequestDto { Code = " animals ", DisplayName = " Động vật " });

        Assert.Equal("animals", added!.Code);
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
}
