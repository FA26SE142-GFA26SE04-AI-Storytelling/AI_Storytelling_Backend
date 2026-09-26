using System.IO;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ExistingStories.DTOs;
using StoryPlatform.Application.Features.ExistingStories.Interfaces;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Controllers;

public sealed class ExistingStoriesControllerTests
{
    private readonly Mock<IExistingStoryService> _existingStoryMock = new();
    private readonly Mock<IExistingStoryEvaluationService> _evaluationMock = new();
    private readonly Mock<IStableVersionArtifactHandoffService> _handoffMock = new();
    private readonly ExistingStoriesController _controller;

    public ExistingStoriesControllerTests()
    {
        _controller = new ExistingStoriesController(
            _existingStoryMock.Object,
            _evaluationMock.Object,
            _handoffMock.Object)
        {
            ControllerContext = ControllerContextForUser(5)
        };
    }

    #region Import Tests

    [Fact]
    public async Task Import_ReturnsOk_WithImportResult()
    {
        var request = new ImportStoryRequestDto
        {
            ChildProfileId = 1,
            InputMethod = "paste",
            Title = "Câu chuyện bé An",
            Content = "Ngày xửa ngày xưa có một chú gấu nhỏ.",
            Language = "vi"
        };
        var expectedResult = new ImportStoryResponseDto
        {
            StoryId = 10,
            StoryVersionId = 1,
            StoryStatus = "Draft",
            EditType = "Initial",
            CanProceed = true
        };

        _existingStoryMock
            .Setup(s => s.ImportAsync(5, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var actionResult = await _controller.Import(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<ImportStoryResponseDto>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal(10, apiResponse.Data?.StoryId);
        Assert.Equal("Import truyện thành công.", apiResponse.Message);
    }

    [Fact]
    public async Task ImportFile_ReturnsBadRequest_WhenFileIsNull()
    {
        var form = new ImportExistingStoryFileForm
        {
            File = null!,
            ChildProfileId = 1
        };

        var actionResult = await _controller.ImportFile(form, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("File upload không được để trống.", apiResponse.Message);
    }

    [Fact]
    public async Task ImportFile_ReturnsBadRequest_WhenFileIsEmpty()
    {
        var emptyFileMock = new Mock<IFormFile>();
        emptyFileMock.Setup(f => f.Length).Returns(0);

        var form = new ImportExistingStoryFileForm
        {
            File = emptyFileMock.Object,
            ChildProfileId = 1
        };

        var actionResult = await _controller.ImportFile(form, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("File upload không được để trống.", apiResponse.Message);
    }

    [Fact]
    public async Task ImportFile_ReturnsBadRequest_WhenFileExceeds5MB()
    {
        var largeFileMock = new Mock<IFormFile>();
        largeFileMock.Setup(f => f.Length).Returns(5_000_001);

        var form = new ImportExistingStoryFileForm
        {
            File = largeFileMock.Object,
            ChildProfileId = 1
        };

        var actionResult = await _controller.ImportFile(form, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("File upload vượt quá giới hạn 5 MB.", apiResponse.Message);
    }

    [Fact]
    public async Task ImportFile_ReturnsOk_WhenFileIsValid()
    {
        var fileContent = "Hôm nay trời nắng đẹp, bé gấu đi dạo trong rừng.";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(fileContent));
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.Length).Returns(stream.Length);
        fileMock.Setup(f => f.FileName).Returns("truyen.txt");
        fileMock.Setup(f => f.ContentType).Returns("text/plain");
        fileMock.Setup(f => f.OpenReadStream()).Returns(stream);

        var form = new ImportExistingStoryFileForm
        {
            File = fileMock.Object,
            ChildProfileId = 1,
            Title = "Bé Gấu",
            Language = "vi"
        };

        var expectedResult = new ImportStoryResponseDto
        {
            StoryId = 12,
            StoryVersionId = 1,
            StoryStatus = "Draft",
            EditType = "Initial",
            CanProceed = true
        };

        _existingStoryMock
            .Setup(s => s.ImportDocumentAsync(5, It.IsAny<ImportStoryDocumentRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var actionResult = await _controller.ImportFile(form, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<ImportStoryResponseDto>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal(12, apiResponse.Data?.StoryId);
        Assert.Equal("Import file truyện thành công.", apiResponse.Message);
    }

    #endregion

    #region Evaluation Tests

    [Fact]
    public async Task Evaluate_ReturnsOk_WithEvaluationResult()
    {
        var request = new EvaluateExistingStoryRequestDto { StoryVersionId = 1 };
        var expectedEvaluation = new ExistingStoryEvaluationDto
        {
            StoryId = 10,
            StoryVersionId = 1,
            Decision = ExistingStoryDecision.Suitable,
            SafetyScore = 1.0m,
            CanKeepOriginal = true,
            ReadabilityFkgl = 2.5m,
            ReadabilityFre = 85.0m
        };

        _evaluationMock
            .Setup(e => e.EvaluateAsync(5, 10, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedEvaluation);

        var actionResult = await _controller.Evaluate(10, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<ExistingStoryEvaluationDto>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal(ExistingStoryDecision.Suitable, apiResponse.Data?.Decision);
        Assert.Equal("Đánh giá truyện hoàn tất.", apiResponse.Message);
    }

    [Fact]
    public async Task GetLatestEvaluation_ReturnsOk_WithCachedResult()
    {
        var expectedEvaluation = new ExistingStoryEvaluationDto
        {
            StoryId = 10,
            StoryVersionId = 1,
            Decision = ExistingStoryDecision.AdaptRecommended,
            SafetyScore = 0.9m,
            CanKeepOriginal = true
        };

        _evaluationMock
            .Setup(e => e.GetLatestAsync(5, 10, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedEvaluation);

        var actionResult = await _controller.GetLatestEvaluation(10, 1, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<ExistingStoryEvaluationDto>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal(ExistingStoryDecision.AdaptRecommended, apiResponse.Data?.Decision);
        Assert.Equal("Lấy đánh giá mới nhất.", apiResponse.Message);
    }

    #endregion

    #region Versioning & Decision Tests

    [Fact]
    public async Task Adapt_ReturnsBadRequest_WhenStoryIdMismatch()
    {
        var request = new AdaptExistingStoryRequestDto
        {
            StoryId = 999, // Mismatched
            BaseStoryVersionId = 1,
            Guideline = "Đơn giản hóa từ vựng"
        };

        var actionResult = await _controller.Adapt(10, request, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("StoryId không khớp.", apiResponse.Message);
    }

    [Fact]
    public async Task Adapt_ReturnsOk_AndQueuesArtifacts_WhenEvaluationIsSuitable()
    {
        var request = new AdaptExistingStoryRequestDto
        {
            StoryId = 10,
            BaseStoryVersionId = 1,
            Guideline = "Đơn giản hóa từ vựng cho bé 6 tuổi"
        };
        var mutationResponse = new VersionMutationResponseDto
        {
            StoryId = 10,
            StoryVersionId = 2,
            StoryStatus = "Draft",
            EditType = "AiRefined"
        };
        var evaluationResponse = new ExistingStoryEvaluationDto
        {
            StoryId = 10,
            StoryVersionId = 2,
            Decision = ExistingStoryDecision.Suitable
        };

        _existingStoryMock
            .Setup(s => s.AdaptAsync(5, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mutationResponse);

        _evaluationMock
            .Setup(e => e.EvaluateAsync(5, 10, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evaluationResponse);

        var actionResult = await _controller.Adapt(10, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<VersionMutationResponseDto>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal(2, apiResponse.Data?.StoryVersionId);
        Assert.Equal("Suitable", apiResponse.Data?.Decision);

        // Verify artifact handoff was queued
        _handoffMock.Verify(h => h.QueueArtifactsAsync(10, 2, 5, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Adapt_ReturnsOk_WithoutQueueingArtifacts_WhenEvaluationIsAdaptRecommended()
    {
        var request = new AdaptExistingStoryRequestDto
        {
            StoryId = 10,
            BaseStoryVersionId = 1,
            Guideline = "Viết lại"
        };
        var mutationResponse = new VersionMutationResponseDto
        {
            StoryId = 10,
            StoryVersionId = 2,
            StoryStatus = "Draft",
            EditType = "AiRefined"
        };
        var evaluationResponse = new ExistingStoryEvaluationDto
        {
            StoryId = 10,
            StoryVersionId = 2,
            Decision = ExistingStoryDecision.AdaptRecommended
        };

        _existingStoryMock
            .Setup(s => s.AdaptAsync(5, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mutationResponse);

        _evaluationMock
            .Setup(e => e.EvaluateAsync(5, 10, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evaluationResponse);

        var actionResult = await _controller.Adapt(10, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<VersionMutationResponseDto>>(okResult.Value);
        Assert.Equal("AdaptRecommended", apiResponse.Data?.Decision);

        // Should NOT queue handoff if decision is not Suitable
        _handoffMock.Verify(h => h.QueueArtifactsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateContent_ReturnsBadRequest_WhenStoryIdMismatch()
    {
        var request = new ManualEditRequestDto
        {
            StoryId = 999,
            BaseStoryVersionId = 1,
            Title = "Tiêu đề mới",
            Content = "Nội dung mới"
        };

        var actionResult = await _controller.UpdateContent(10, request, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("StoryId không khớp.", apiResponse.Message);
    }

    [Fact]
    public async Task UpdateContent_ReturnsOk_AndQueuesArtifacts_WhenEvaluationIsSuitable()
    {
        var request = new ManualEditRequestDto
        {
            StoryId = 10,
            BaseStoryVersionId = 1,
            Title = "Bé Gấu học chia sẻ",
            Content = "Bé Gấu tặng bạn quả táo ngọt ngào.",
            Lesson = "Chia sẻ với bạn bè"
        };
        var mutationResponse = new VersionMutationResponseDto
        {
            StoryId = 10,
            StoryVersionId = 2,
            StoryStatus = "Draft",
            EditType = "HumanEdited"
        };
        var evaluationResponse = new ExistingStoryEvaluationDto
        {
            StoryId = 10,
            StoryVersionId = 2,
            Decision = ExistingStoryDecision.Suitable
        };

        _existingStoryMock
            .Setup(s => s.UpdateContentAsync(5, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mutationResponse);

        _evaluationMock
            .Setup(e => e.EvaluateAsync(5, 10, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evaluationResponse);

        var actionResult = await _controller.UpdateContent(10, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<VersionMutationResponseDto>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal(2, apiResponse.Data?.StoryVersionId);

        _handoffMock.Verify(h => h.QueueArtifactsAsync(10, 2, 5, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task KeepOriginal_ReturnsBadRequest_WhenStoryIdMismatch()
    {
        var request = new KeepOriginalRequestDto
        {
            StoryId = 999,
            StoryVersionId = 1,
            OverrideReason = "Phụ huynh đồng ý giữ nguyên"
        };

        var actionResult = await _controller.KeepOriginal(10, request, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("StoryId không khớp.", apiResponse.Message);
    }

    [Fact]
    public async Task KeepOriginal_ReturnsOk_AndAlwaysQueuesArtifacts()
    {
        var request = new KeepOriginalRequestDto
        {
            StoryId = 10,
            StoryVersionId = 1,
            OverrideReason = "Gia đình muốn giữ nguyên tác câu chuyện cổ tích dân gian."
        };
        var mutationResponse = new VersionMutationResponseDto
        {
            StoryId = 10,
            StoryVersionId = 1,
            StoryStatus = "Draft",
            EditType = "Initial",
            Decision = "KeepOriginal"
        };

        _existingStoryMock
            .Setup(s => s.KeepOriginalAsync(5, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mutationResponse);

        var actionResult = await _controller.KeepOriginal(10, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<VersionMutationResponseDto>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal("Giữ nguyên bản gốc.", apiResponse.Message);

        _handoffMock.Verify(h => h.QueueArtifactsAsync(10, 1, 5, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Archive_ReturnsBadRequest_WhenStoryIdMismatch()
    {
        var request = new ArchiveExistingStoryRequestDto
        {
            StoryId = 999,
            Reason = "Nội dung không còn phù hợp"
        };

        var actionResult = await _controller.Archive(10, request, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("StoryId không khớp.", apiResponse.Message);
    }

    [Fact]
    public async Task Archive_ReturnsOk_WhenSuccessful()
    {
        var request = new ArchiveExistingStoryRequestDto
        {
            StoryId = 10,
            Reason = "Nội dung vi phạm an toàn gia đình"
        };

        _existingStoryMock
            .Setup(s => s.ArchiveAsync(5, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var actionResult = await _controller.Archive(10, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<bool>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.True(apiResponse.Data);
        Assert.Equal("Lưu trữ truyện.", apiResponse.Message);
    }

    #endregion

    private static ControllerContext ControllerContextForUser(int userId) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, "Parent") }, "TestAuth"))
        }
    };
}
