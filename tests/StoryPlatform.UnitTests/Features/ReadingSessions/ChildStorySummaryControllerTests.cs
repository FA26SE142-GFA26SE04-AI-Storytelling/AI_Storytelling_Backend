using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public sealed class ChildStorySummaryControllerTests
{
    private static ChildStorySummaryController Controller(IChildStorySummaryService service) => new(service)
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
    public void Questions_ReplacesFlags_AndKeepsAudioInMemory()
    {
        var methods = typeof(ChildStorySummaryController).GetMethods();
        Assert.DoesNotContain(methods, method => method.GetCustomAttribute<HttpPostAttribute>()?.Template == "flags");
        var ask = typeof(ChildStorySummaryController).GetMethod(nameof(ChildStorySummaryController.Ask))!;
        Assert.Equal("questions", ask.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Contains("multipart/form-data", ask.GetCustomAttribute<ConsumesAttribute>()!.ContentTypes);
        Assert.True(ask.GetCustomAttribute<RequestFormLimitsAttribute>()!.MemoryBufferThreshold >= StorySummaryPolicy.MaxAudioBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ask_PassesOptionalAudioAndPictureFieldsToService(bool withAudio)
    {
        var service = new Mock<IChildStorySummaryService>(MockBehavior.Strict);
        StorySummaryQuestionRequest? received = null;
        service.Setup(x => x.AskAsync(100, 5, It.IsAny<StorySummaryQuestionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, StorySummaryQuestionRequest, CancellationToken>((_, _, request, _) => received = request)
            .ReturnsAsync(new StorySummaryQuestionSentDto { SummaryId = 7, QuestionId = 9, Message = StorySummaryPolicy.SentMessage });
        using var stream = new MemoryStream([1, 2, 3]);
        var form = new StorySummaryQuestionForm
        {
            SceneId = 4, BeatOrder = 2, Text = "Em hỏi bố mẹ",
            Audio = withAudio ? new FormFile(stream, 0, 3, "audio", "question.webm") { Headers = new HeaderDictionary(), ContentType = "audio/webm" } : null
        };
        var response = await Controller(service.Object).Ask(5, form, default);
        var result = Assert.IsType<ApiResponse<StorySummaryQuestionSentDto>>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal(9, result.Data!.QuestionId);
        Assert.Equal((4, 2, form.Text), (received!.SceneId, received.BeatOrder, received.Text));
        Assert.Equal(withAudio ? new byte[] { 1, 2, 3 } : [], received.Audio);
    }

    [Fact]
    public async Task Ask_OversizedFile_IsRejectedBeforeReadingOrCallingService()
    {
        var service = new Mock<IChildStorySummaryService>(MockBehavior.Strict);
        var audio = new Mock<IFormFile>(MockBehavior.Strict);
        audio.SetupGet(x => x.Length).Returns(StorySummaryPolicy.MaxAudioBytes + 1L);
        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            Controller(service.Object).Ask(5, new StorySummaryQuestionForm { SceneId = 1, Audio = audio.Object }, default));
        Assert.Equal(StorySummaryPolicy.AudioTooLargeMessage, error.Message);
        service.VerifyNoOtherCalls();
        audio.Verify(x => x.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
