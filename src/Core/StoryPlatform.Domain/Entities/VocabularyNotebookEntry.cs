using System;

namespace StoryPlatform.Domain.Entities;

public class VocabularyNotebookEntry : BaseEntity
{
    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public int StoryVocabularyId { get; set; }
    public virtual StoryVocabulary? StoryVocabulary { get; set; }

    public DateTime CollectedAt { get; set; }

    /// <summary>Lúc trẻ bỏ từ khỏi Sổ tay; null = đang hiện. Lưu lại thì đặt về null (không tạo dòng mới).</summary>
    public DateTime? RemovedAt { get; set; }
}
