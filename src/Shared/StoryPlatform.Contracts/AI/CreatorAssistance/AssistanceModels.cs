namespace StoryPlatform.Contracts.AI.CreatorAssistance;

public sealed record EditorSnapshot(string Title, string Content);
public sealed record AssistanceTarget(string Kind, int Start, int Length);
public sealed record AssistanceConstraints(string[] Keep, string[] Avoid);
public sealed record AssistanceChoice(string Id, string Text);
public sealed record AssistanceQuestion(string Id, string Text, AssistanceChoice[] Choices);
public sealed record AssistanceOption(string Id, string Summary, string Direction);
public sealed record AssistanceFinding(string Id, int Start, int Length, string Quote, string Comment, string SuggestedGoal);
public sealed record AssistancePatch(string Kind, int Start, int Length, string Text, string Quote);

/// <summary>Internal Core-to-AI contract. The AI service never receives persistence access.</summary>
public sealed record AssistanceChildPolicy(int MaxStoryWords, int? ReadingLevel,
    string[] BlockedCategories, string[] RestrictedCategories, decimal? ReadabilityThreshold, decimal? SafetyThreshold);

public sealed record AssistanceAiRequest
{
    public AssistanceChildPolicy? ChildPolicy { get; init; }
    public string Mode { get; init; } = "Suggestions";
    public string EntryPoint { get; init; } = "ContinueWriting";
    public EditorSnapshot Snapshot { get; init; } = new("", "");
    public AssistanceTarget Target { get; init; } = new("cursor", 0, 0);
    public string Language { get; init; } = "vi";
    public string AgeBand { get; init; } = "6-8";
    public string Goal { get; init; } = "";
    public AssistanceConstraints Constraints { get; init; } = new([], []);
    public string? Answer { get; init; }
    public string? ConfirmedDirection { get; init; }
    public int MaximumTrialCharacters { get; init; } = 3000;
}

public sealed record AssistanceAiResult
{
    public string ResultType { get; init; } = "";
    public AssistanceQuestion? Question { get; init; }
    public AssistanceOption[] Options { get; init; } = [];
    public AssistanceFinding[] Findings { get; init; } = [];
    public AssistancePatch? Patch { get; init; }
    public string[] ChecksPerformed { get; init; } = [];
    public string[] Warnings { get; init; } = [];
    public string[] Assumptions { get; init; } = [];
    public string? Coverage { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? PromptVersion { get; init; }
}
