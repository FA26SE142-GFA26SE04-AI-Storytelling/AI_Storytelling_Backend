namespace StoryPlatform.Application.Features.ReadingSessions.Interfaces;

/// <summary>Một chữ nhận dạng được; Confidence null khi dịch vụ không trả độ tin cậy.</summary>
public sealed record RecognizedWord(string Text, double? Confidence);

/// <summary>Nhận dạng giọng nói tiếng Việt trong bộ nhớ; service gọi quyết định việc lưu bản ghi.</summary>
public interface ISpeechRecognizer
{
    Task<IReadOnlyList<RecognizedWord>> RecognizeAsync(byte[] audio, CancellationToken cancellationToken = default);
}
