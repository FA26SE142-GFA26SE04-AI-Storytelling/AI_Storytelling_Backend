namespace StoryPlatform.Application.Features.ReadingSessions.Interfaces;

/// <summary>Một chữ nhận dạng được; Confidence null khi dịch vụ không trả độ tin cậy.</summary>
public sealed record RecognizedWord(string Text, double? Confidence);

/// <summary>Nhận dạng giọng nói tiếng Việt. Âm thanh chỉ dùng trong bộ nhớ, không lưu.</summary>
public interface ISpeechRecognizer
{
    Task<IReadOnlyList<RecognizedWord>> RecognizeAsync(byte[] audio, CancellationToken cancellationToken = default);
}
