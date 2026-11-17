namespace StoryPlatform.Domain.Enums;

/// <summary>Cảm xúc trẻ chọn (1–2) sau khi đọc xong một truyện. Lưu dạng tên trong DB (text[]).</summary>
public enum StoryEmotion
{
    Happy = 1,
    Funny = 2,
    Surprised = 3,
    Excited = 4,
    Touched = 5,
    Sad = 6,
    Scared = 7
}
