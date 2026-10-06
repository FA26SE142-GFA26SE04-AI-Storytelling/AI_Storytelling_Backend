using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using Xunit;

namespace StoryPlatform.UnitTests.Controllers;

public class SupervisionControllerTests
{
    private readonly Mock<ISupervisionService> _service = new();
    private readonly SupervisionController _sut;

    public SupervisionControllerTests()
    {
        _sut = new SupervisionController(_service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, "2") }, "TestAuth"))
                }
            }
        };
    }

    [Fact]
    public async Task ClaimInvitation_DelegatesWithCurrentUser()
    {
        var expected = new ClaimInvitationResultDto
        {
            MaskedEmail = "i*****@example.com",
            OtpExpiresAt = DateTime.UtcNow.AddMinutes(10)
        };
        _service.Setup(s => s.ClaimInvitationAsync("CODE", 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var response = await _sut.ClaimInvitation(
            new ClaimInvitationRequestDto { InvitationCode = "CODE" }, CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(response.Result);
        Assert.Same(expected, Assert.IsType<ApiResponse<ClaimInvitationResultDto>>(result.Value).Data);
    }

    [Fact]
    public async Task VerifyInvitationOtp_DelegatesWithCurrentUser()
    {
        var expected = new InvitationPreviewDto { InvitationId = 1, ChildNickname = "Bé An" };
        _service.Setup(s => s.VerifyInvitationOtpAsync("CODE", "123456", 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var response = await _sut.VerifyInvitationOtp(
            new VerifyInvitationOtpRequestDto { InvitationCode = "CODE", Otp = "123456" },
            CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(response.Result);
        Assert.Same(expected, Assert.IsType<ApiResponse<InvitationPreviewDto>>(result.Value).Data);
    }

    [Fact]
    public async Task RejectInvitation_DelegatesWithCurrentUser()
    {
        var response = await _sut.RejectInvitation(
            new RejectInvitationRequestDto { InvitationCode = "CODE" }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(response.Result);
        _service.Verify(s => s.RejectInvitationAsync(
            "CODE", 2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelOwnershipTransfer_DelegatesWithCurrentUser()
    {
        var expected = new OwnershipTransferRequestDto { Id = 50, Status = "Cancelled" };
        _service.Setup(s => s.CancelOwnershipTransferAsync(
                50, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var response = await _sut.CancelOwnershipTransfer(50, CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(response.Result);
        Assert.Same(expected, Assert.IsType<ApiResponse<OwnershipTransferRequestDto>>(result.Value).Data);
    }

    [Theory]
    [InlineData(nameof(SupervisionController.ClaimInvitation), "invitations/claim")]
    [InlineData(nameof(SupervisionController.VerifyInvitationOtp), "invitations/verify-otp")]
    [InlineData(nameof(SupervisionController.RejectInvitation), "invitations/reject")]
    [InlineData(nameof(SupervisionController.CancelOwnershipTransfer), "ownership-transfers/{ownershipTransferRequestId:int}/cancel")]
    public void NewEndpoints_DeclareRouteAndParentRole(string methodName, string template)
    {
        var method = typeof(SupervisionController).GetMethod(methodName)!;

        var post = Assert.Single(method.GetCustomAttributes<HttpPostAttribute>());
        Assert.Equal(template, post.Template);
        var authorize = Assert.Single(method.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal("Parent", authorize.Roles);
    }
}
