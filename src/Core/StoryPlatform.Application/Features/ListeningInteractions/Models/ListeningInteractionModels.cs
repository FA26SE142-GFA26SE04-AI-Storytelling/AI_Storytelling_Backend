using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ListeningInteractions.Models;

public sealed record ListeningPlanItem(ListeningInteractionType Type, int Count);

/// <summary>Kế hoạch do backend tính: có loại nào, mỗi loại bao nhiêu. Thứ tự Items = thứ tự gọi AI.</summary>
public sealed record ListeningInteractionPlan(int Slots, IReadOnlyList<ListeningPlanItem> Items, IReadOnlyList<string> DialogueBlockIds)
{
    public static readonly ListeningInteractionPlan Empty = new(0, [], []);
}

/// <summary>Lỗi kiểm tra; cũng là một phần tử của previousErrors gửi lại cho AI.</summary>
public sealed record ListeningValidationError(string Code, int? ItemIndex = null, string? Field = null);

public sealed record ListeningOptionDraft
{
    public string Text { get; init; } = string.Empty;
    public string? Feedback { get; init; }
    public bool IsMatch { get; init; }
    public int? CorrectOrder { get; init; }
    /// <summary>EventOrdering: đoạn chứa sự kiện.</summary>
    public string? BlockId { get; init; }
    public string? SourceQuote { get; init; }
    public int? SourceStart { get; init; }
    public int? SourceEnd { get; init; }
    public string? ImageDescription { get; init; }
}

/// <summary>
/// Hình dạng chung của một tương tác trước khi lưu: kết quả AI, dữ liệu đọc từ DB và bản người duyệt sửa
/// đều đổi về đây để dùng chung ListeningInteractionRules. Offset null = chưa được mapper tính.
/// </summary>
public sealed record ListeningInteractionDraft
{
    public ListeningInteractionType Type { get; init; }
    public string AnchorBlockId { get; init; } = string.Empty;
    public int? AnchorOffset { get; init; }
    public CheckpointKind? Kind { get; init; }
    public string? Prompt { get; init; }
    public string? SourceQuote { get; init; }
    public int? SourceStart { get; init; }
    public int? SourceEnd { get; init; }
    public string? CharacterName { get; init; }
    public string? HiddenWord { get; init; }
    public string? Hint { get; init; }
    public string? RevealBlockId { get; init; }
    public int? RevealOffset { get; init; }
    public string? Explanation { get; init; }
    public string? MatchFeedback { get; init; }
    public string? NoMatchFeedback { get; init; }
    public string? WaitFeedback { get; init; }
    public IReadOnlyList<ListeningOptionDraft> Options { get; init; } = [];
}

public sealed record ListeningRuleContext
{
    public ListeningInteractionType Type { get; init; }
    public int Count { get; init; }
    public AgeBand AgeBand { get; init; }
    public string Content { get; init; } = string.Empty;
    public IReadOnlyList<StoryTextBlock> Blocks { get; init; } = [];
    /// <summary>Neo của các loại khác đã lưu trên cùng version.</summary>
    public IReadOnlyCollection<int> OccupiedAnchorOffsets { get; init; } = [];
    public IReadOnlyCollection<string> DialogueBlockIds { get; init; } = [];
    /// <summary>Văn bản để kiểm tra NOT_GROUNDED, thường là Content + Lesson.</summary>
    public string GroundingSource { get; init; } = string.Empty;
    /// <summary>Guardrail an toàn của Phase 4 (IsArtifactSafe), truyền từ service gọi.</summary>
    public Func<string, bool> IsSafe { get; init; } = _ => true;
    public IReadOnlyCollection<string> GradingWords { get; init; } = ListeningInteractionRules.DefaultGradingWords;
}
