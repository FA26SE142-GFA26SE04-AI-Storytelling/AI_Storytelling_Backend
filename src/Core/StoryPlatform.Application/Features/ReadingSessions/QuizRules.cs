using System.Globalization;
using System.Text;
using System.Text.Json;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions;

public static class QuizRules
{
    private static readonly HashSet<string> TrueWords = new(StringComparer.Ordinal) { "true", "dung" };
    private static readonly HashSet<string> FalseWords = new(StringComparer.Ordinal) { "false", "sai" };

    public static bool IsCorrect(QuizType type, string? correctAnswer, string? given)
    {
        if (string.IsNullOrWhiteSpace(correctAnswer) || string.IsNullOrWhiteSpace(given))
        {
            return false;
        }

        return type switch
        {
            QuizType.TrueFalse => MatchTrueFalse(correctAnswer, given),
            QuizType.ShortAnswer => MatchShortAnswer(correctAnswer, given),
            QuizType.MultipleChoice => Normalize(correctAnswer) == Normalize(given),
            _ => false
        };
    }

    public static IReadOnlyList<string> ParseChoices(QuizType type, string? choicesJson)
    {
        if (type == QuizType.ShortAnswer)
        {
            return new List<string>();
        }

        var choices = new List<string>();
        if (!string.IsNullOrWhiteSpace(choicesJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(choicesJson);
                if (parsed != null)
                {
                    choices.AddRange(parsed.Where(choice => !string.IsNullOrWhiteSpace(choice))
                        .Select(choice => choice.Trim()));
                }
            }
            catch (JsonException)
            {
                // Dữ liệu cũ không đọc được: coi như không có lựa chọn.
            }
        }

        if (type == QuizType.TrueFalse && choices.Count == 0)
        {
            choices.Add("Đúng");
            choices.Add("Sai");
        }

        return choices;
    }

    /// <summary>Bỏ dấu, bỏ dấu câu, thường hóa và gộp khoảng trắng ("Đồng  Tháp." → "dong thap").</summary>
    public static string Fold(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (ch == 'đ' || ch == 'Đ')
            {
                builder.Append('d');
            }
            else if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (char.IsWhiteSpace(ch))
            {
                builder.Append(' ');
            }
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant().Normalize(NormalizationForm.FormC);

    private static IEnumerable<string> AcceptedAnswers(string correctAnswer) =>
        correctAnswer.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool MatchShortAnswer(string correctAnswer, string given)
    {
        var actual = Fold(given);
        return actual.Length > 0 && AcceptedAnswers(correctAnswer).Any(answer => Fold(answer) == actual);
    }

    private static bool MatchTrueFalse(string correctAnswer, string given)
    {
        var expected = ToBool(correctAnswer);
        var actual = ToBool(given);
        return expected.HasValue && actual.HasValue && expected.Value == actual.Value;
    }

    private static bool? ToBool(string value)
    {
        var folded = Fold(value);
        if (TrueWords.Contains(folded))
        {
            return true;
        }

        return FalseWords.Contains(folded) ? false : null;
    }
}
