using StoryPlatform.Application.Common.Exceptions;

namespace StoryPlatform.Application.Features.ReadingSessions;

/// <summary>Dịch vụ nhận dạng giọng nói lỗi hoặc quá thời gian; app cho trẻ đọc tiếp truyện.</summary>
public sealed class SpeechRecognitionUnavailableException : AppException
{
    public SpeechRecognitionUnavailableException() : base(ReadAloudPolicy.UnavailableMessage, 503)
    {
    }
}
