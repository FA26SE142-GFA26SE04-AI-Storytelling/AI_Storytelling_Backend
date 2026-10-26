namespace StoryPlatform.Contracts.AI.Models;

/// <summary>Only fixed validation codes may cross the service boundary or enter retry prompts.</summary>
public static class LearningValidationFeedback
{
    public static string? Normalize(string? code) => code switch
    {
        "LEARNING_JSON_INVALID" or "LEARNING_EVIDENCE_REQUIRED" or "LEARNING_EVIDENCE_NOT_IN_STORY" or
        "VOCABULARY_NOT_GROUNDED_OR_DUPLICATE" or "VOCABULARY_EVIDENCE_MISMATCH" or
        "QUIZ_GROUNDING_FIELDS_INVALID" or "QUIZ_ITEM_REQUIRED" or "QUIZ_QUESTION_REQUIRED" or "QUIZ_QUESTION_DUPLICATE" or
        "QUIZ_EXPLANATION_REQUIRED" or "QUIZ_HINT_REQUIRED" or "QUIZ_LEARNING_GOAL_INVALID" or
        "QUIZ_VOCABULARY_DEPENDENCY_INVALID" or "QUIZ_OPTIONS_INVALID" or
        "QUIZ_TRUE_FALSE_INVALID" or "QUIZ_ANSWER_INVALID" or "QUIZ_TYPE_COVERAGE_INVALID" or "QUIZ_QUESTION_RELEVANCE_INVALID" or "QUIZ_VALIDATION_FAILED" or "LEARNING_BATCH_COUNT_INVALID" or
        "LEARNING_SHORTFALL_REASON_TOO_LONG" or "LEARNING_SAFETY_INVALID" => code,
        _ => null
    };

    public static string Instruction(string? code) => Normalize(code) switch
    {
        "QUIZ_TYPE_COVERAGE_INVALID" => "Include every type in requiredQuestionTypes at least once. Learning goals are separate from question types. Keep all evidence and answer requirements.",
        "QUIZ_QUESTION_RELEVANCE_INVALID" => "Mention the supplied story's character, action or object directly in the question, not only in sourceQuote. Do not ask about unrelated subjects.",
        "QUIZ_VALIDATION_FAILED" => "Include every type in requiredQuestionTypes at least once and keep every question directly related to the supplied story. Recheck answers, options and evidence.",
        "LEARNING_EVIDENCE_REQUIRED" or "LEARNING_EVIDENCE_NOT_IN_STORY" =>
            "Copy sourceQuote verbatim from the supplied story body, including punctuation and accents; do not paraphrase or join separated passages.",
        "VOCABULARY_NOT_GROUNDED_OR_DUPLICATE" or "VOCABULARY_EVIDENCE_MISMATCH" =>
            "Choose distinct vocabulary terms present in the story and in each item's exact sourceQuote.",
        "QUIZ_OPTIONS_INVALID" =>
            "Use distinct non-empty options; use a zero-based correctOptionIndex; correctAnswer must equal that option exactly; supply one non-empty optionFeedback per option.",
        "QUIZ_GROUNDING_FIELDS_INVALID" =>
            "Use distinct questions, non-empty explanation and hint, and an allowed learningGoal. Do not repeat excluded questions.",
        "QUIZ_ITEM_REQUIRED" => "Do not include null items; each array item must be a complete quiz object.",
        "QUIZ_QUESTION_REQUIRED" => "Supply non-empty question text for every quiz item.",
        "QUIZ_QUESTION_DUPLICATE" => "Replace repeated questions, including questions in excludedQuestions. Differences in capitalization, whitespace or punctuation do not make a new question.",
        "QUIZ_EXPLANATION_REQUIRED" => "Supply a non-empty explanation for every answer, supported by the exact sourceQuote.",
        "QUIZ_HINT_REQUIRED" => "Supply a non-empty hint for every question; guide the child without revealing the answer.",
        "QUIZ_LEARNING_GOAL_INVALID" => "Use exactly one lowercase learningGoal value: recall, sequence, cause, inference or vocabulary. Do not translate or invent labels.",
        "QUIZ_VOCABULARY_DEPENDENCY_INVALID" =>
            "Set learningGoal to vocabulary and include a supplied validated vocabulary term in the question or exact evidence quotation.",
        "QUIZ_TRUE_FALSE_INVALID" => "For true_false use the string true or false as correctAnswer, not a localized synonym.",
        "QUIZ_ANSWER_INVALID" => "Use an allowed question type and a non-empty answer; short_answer must have a textual correctAnswer.",
        "LEARNING_BATCH_COUNT_INVALID" => "Never exceed targetCount. If fewer items are supported, include a non-empty shortfallReason.",
        "LEARNING_SHORTFALL_REASON_TOO_LONG" => "Limit shortfallReason to at most 300 characters.",
        "LEARNING_SAFETY_INVALID" => "Remove personal contact details and disallowed subject matter while preserving all safety constraints.",
        "LEARNING_JSON_INVALID" => "Return valid JSON matching the structured schema, with a non-null items array; no markdown fences or extra commentary.",
        _ => string.Empty
    };
}
