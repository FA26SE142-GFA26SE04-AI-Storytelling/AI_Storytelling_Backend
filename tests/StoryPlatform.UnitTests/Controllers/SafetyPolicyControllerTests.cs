using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ChildProfiles.Safety.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Safety.Interfaces;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Controllers;

public class SafetyPolicyControllerTests
{
    private readonly Mock<ISafetyPolicyService> _service = new();
    private readonly SafetyPolicyController _sut;

    public SafetyPolicyControllerTests()
    {
        _sut = new SafetyPolicyController(_service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, "9") }, "TestAuth"))
                }
            }
        };
    }

    private static SetSafetyPolicyRequestDto ValidRequest() => new()
    {
        MaxStoryLength = 2000,
        RequiredApprovalMode = ApprovalMode.AlwaysManual,
        ParentalGateEnabled = true,
        ConsentRecorded = true,
        Categories = new List<SetSafetyPolicyCategoryRequestDto>
        {
            new() { ContentCategoryId = 1, Rule = PolicyRule.Allowed }
        }
    };

    [Fact]
    public async Task CreateSafetyPolicy_DelegatesToSetSafetyPolicyAsync_WithCurrentUserId()
    {
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var request = ValidRequest();
        var expected = new SafetyPolicyDto { Id = 1, ChildProfileId = 4, MaxStoryLength = 2000 };
        _service.Setup(s => s.SetSafetyPolicyAsync(4, 9, request, token))
            .ReturnsAsync(expected);

        var response = await _sut.CreateSafetyPolicy(4, request, token);

        var okResult = Assert.IsType<OkObjectResult>(response.Result);
        var apiResponse = Assert.IsType<ApiResponse<SafetyPolicyDto>>(okResult.Value);
        Assert.Same(expected, apiResponse.Data);
        Assert.True(apiResponse.Success);
        Assert.Null(apiResponse.Errors);
        _service.Verify(s => s.SetSafetyPolicyAsync(4, 9, request, token), Times.Once);
    }
}
