using System.Text.Json;

namespace StoryPlatform.Application.Features.CommunityLibrary;

/// <summary>Hằng số của lượt duyệt Community (WF-L4F UC-L4F-02).</summary>
public static class CommunityReviewPolicy
{
    public const int MaxNoteLength = 500;
    public const int MaxReasonLength = 1000;

    public static readonly string[] CriterionCodes = { "safety_baseline", "no_personal_info", "metadata_labels" };

    public static string CriterionLabel(string code) => code switch
    {
        "safety_baseline" => "An toàn theo baseline chặt nhất",
        "no_personal_info" => "Không có thông tin định danh của bé hoặc gia đình",
        "metadata_labels" => "Metadata và nhãn nội dung chính xác",
        _ => code
    };
}

public static class CommunityReviewBlockerCodes
{
    public const string OwnSubmission = "own_submission";
    public const string ChecklistIncomplete = "checklist_incomplete";
    public const string StoryArchived = "story_archived";
    public const string PiiFound = "pii_found";
    public const string NoLabels = "no_labels";
}

/// <summary>Đọc / ghi JSON của lượt duyệt: checklist đang làm (ReviewProgress) và phản hồi cuối (Feedback).</summary>
internal static class CommunityReviewState
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private sealed record ProgressJson(List<CommunityChecklistEntryDto>? Checklist);

    private sealed record FeedbackJson(string Decision, string? Reason, List<CommunityChecklistEntryDto> Checklist);

    /// <summary>Luôn trả đủ 3 tiêu chí theo thứ tự; tiêu chí chưa chấm có Passed = false.</summary>
    public static List<CommunityChecklistEntryDto> ReadChecklist(string? json)
    {
        var saved = new List<CommunityChecklistEntryDto>();
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                saved = JsonSerializer.Deserialize<ProgressJson>(json, Options)?.Checklist ?? new();
            }
            catch (JsonException)
            {
                // Dữ liệu hỏng coi như chưa chấm.
            }
        }

        return CommunityReviewPolicy.CriterionCodes.Select(code =>
        {
            var entry = saved.FirstOrDefault(candidate => candidate.Code == code);
            return new CommunityChecklistEntryDto
            {
                Code = code,
                Label = CommunityReviewPolicy.CriterionLabel(code),
                Passed = entry?.Passed ?? false,
                Note = entry?.Note
            };
        }).ToList();
    }

    public static string WriteChecklist(IEnumerable<CommunityChecklistEntryDto> entries) =>
        JsonSerializer.Serialize(new ProgressJson(entries.ToList()), Options);

    public static string WriteFeedback(string decision, string? reason, IEnumerable<CommunityChecklistEntryDto> checklist) =>
        JsonSerializer.Serialize(new FeedbackJson(decision, reason, checklist.ToList()), Options);
}
