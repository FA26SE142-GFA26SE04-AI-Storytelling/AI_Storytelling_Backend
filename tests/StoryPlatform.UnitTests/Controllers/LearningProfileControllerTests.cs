using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ChildProfiles.Learning.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Learning.Interfaces;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Controllers;

public class LearningProfileControllerTests
{
    private readonly Mock<ILearningProfileService> _service = new();
    private readonly LearningProfileController _sut;

    public LearningProfileControllerTests()
    {
        _sut = new LearningProfileController(_service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, "7") }, "TestAuth"))
                }
            }
        };
    }

    private static SetLearningProfileRequestDto ValidRequest() => new()
    {
        ReadingLevel = 3,
        ComprehensionGoal = "Đọc hiểu cơ bản",
        Topics = new List<SetLearningProfileTopicRequestDto>
        {
            new() { Topic = "Động vật", Relation = TopicRelation.FavoriteTopic }
        }
    };

    [Fact]
    public async Task CreateLearningProfile_DelegatesToSetLearningProfileAsync_WithCurrentUserId()
    {
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var request = ValidRequest();
        var expected = new LearningProfileDto { Id = 1, ChildProfileId = 5, ReadingLevel = 3 };
        _service.Setup(s => s.SetLearningProfileAsync(5, 7, request, token))
            .ReturnsAsync(expected);

        var response = await _sut.CreateLearningProfile(5, request, token);

        var okResult = Assert.IsType<OkObjectResult>(response.Result);
        var apiResponse = Assert.IsType<ApiResponse<LearningProfileDto>>(okResult.Value);
        Assert.Same(expected, apiResponse.Data);
        Assert.True(apiResponse.Success);
        Assert.Null(apiResponse.Errors);
        _service.Verify(s => s.SetLearningProfileAsync(5, 7, request, token), Times.Once);
    }
}
