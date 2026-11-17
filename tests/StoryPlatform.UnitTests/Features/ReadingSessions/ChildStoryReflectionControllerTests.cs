using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public sealed class ChildStoryReflectionControllerTests
{
    [Fact]
    public void Controller_RequiresTheChildSessionPolicy()
    {
        Assert.Equal(ServiceExtensions.ChildSessionPolicy,
            typeof(ChildStoryReflectionController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("emotions")]
    [InlineData("scene")]
    public async Task GetAndSelections_ForwardTheChildSessionRequestAndCancellation(string operation)
    {
        var service = new Mock<IChildStoryReflectionService>(MockBehavior.Strict);
        using var cts = new CancellationTokenSource();
        var expected = new ChildStoryReflectionDto { SessionId = 5 };
        var emotions = new SetStoryEmotionsRequestDto { Emotions = ["Happy"] };
        var scene = new SetFavoriteSceneRequestDto { SceneId = 1 };
        service.Setup(s => s.GetAsync(100, 5, cts.Token)).ReturnsAsync(expected);
        service.Setup(s => s.SetEmotionsAsync(100, 5, emotions, cts.Token)).ReturnsAsync(expected);
        service.Setup(s => s.SetFavoriteSceneAsync(100, 5, scene, cts.Token)).ReturnsAsync(expected);
        var controller = Controller(service.Object);

        var result = operation switch
        {
            "get" => await controller.Get(5, cts.Token),
            "emotions" => await controller.SetEmotions(5, emotions, cts.Token),
            _ => await controller.SetFavoriteScene(5, scene, cts.Token)
        };

        var response = Assert.IsType<ApiResponse<ChildStoryReflectionDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Same(expected, response.Data);
        service.Verify(s => s.GetAsync(100, 5, cts.Token), operation == "get" ? Times.Once() : Times.Never());
        service.Verify(s => s.SetEmotionsAsync(100, 5, emotions, cts.Token), operation == "emotions" ? Times.Once() : Times.Never());
        service.Verify(s => s.SetFavoriteSceneAsync(100, 5, scene, cts.Token), operation == "scene" ? Times.Once() : Times.Never());
    }

    private static ChildStoryReflectionController Controller(IChildStoryReflectionService service) => new(service)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "100")], "test"))
            }
        }
    };

    [Fact]
    public void Routes_AreUnderTheReadingSession()
    {
        var type = typeof(ChildStoryReflectionController);
        Assert.Equal("api/v1/child/reading/sessions/{sessionId:int}/reflection", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("emotions", type.GetMethod(nameof(ChildStoryReflectionController.SetEmotions))!.GetCustomAttribute<HttpPutAttribute>()!.Template);
        Assert.Equal("favorite-scene", type.GetMethod(nameof(ChildStoryReflectionController.SetFavoriteScene))!.GetCustomAttribute<HttpPutAttribute>()!.Template);
        var note = type.GetMethod(nameof(ChildStoryReflectionController.SendNote))!;
        Assert.Equal("note", note.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Contains("multipart/form-data", note.GetCustomAttribute<ConsumesAttribute>()!.ContentTypes);
        Assert.True(note.GetCustomAttribute<RequestFormLimitsAttribute>()!.MemoryBufferThreshold >= StorySummaryPolicy.MaxAudioBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendNote_PassesTextAndOptionalAudio(bool withAudio)
    {
        var service = new Mock<IChildStoryReflectionService>(MockBehavior.Strict);
        StoryReflectionNoteRequest? received = null;
        service.Setup(x => x.SendNoteAsync(100, 5, It.IsAny<StoryReflectionNoteRequest>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, StoryReflectionNoteRequest, CancellationToken>((_, _, request, _) => received = request)
            .ReturnsAsync(new ChildStoryReflectionDto { SessionId = 5, Message = StoryReflectionPolicy.NoteSentMessage });
        using var stream = new MemoryStream([1, 2, 3]);
        var form = new StoryReflectionNoteForm
        {
            Text = "Con thích mèo",
            Audio = withAudio ? new FormFile(stream, 0, 3, "audio", "note.webm") { Headers = new HeaderDictionary(), ContentType = "audio/webm" } : null
        };

        var response = await Controller(service.Object).SendNote(5, form, default);

        var result = Assert.IsType<ApiResponse<ChildStoryReflectionDto>>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal(5, result.Data!.SessionId);
        Assert.Equal("Con thích mèo", received!.Text);
        Assert.Equal(withAudio ? new byte[] { 1, 2, 3 } : [], received.Audio);
        Assert.Equal(withAudio ? "audio/webm" : null, received.ContentType);
    }

    [Fact]
    public async Task SendNote_OversizedFile_IsRejectedBeforeReadingOrCallingService()
    {
        var service = new Mock<IChildStoryReflectionService>(MockBehavior.Strict);
        var audio = new Mock<IFormFile>(MockBehavior.Strict);
        audio.SetupGet(x => x.Length).Returns(StorySummaryPolicy.MaxAudioBytes + 1L);

        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            Controller(service.Object).SendNote(5, new StoryReflectionNoteForm { Audio = audio.Object }, default));

        Assert.Equal(StorySummaryPolicy.AudioTooLargeMessage, error.Message);
        service.VerifyNoOtherCalls();
        audio.Verify(x => x.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
