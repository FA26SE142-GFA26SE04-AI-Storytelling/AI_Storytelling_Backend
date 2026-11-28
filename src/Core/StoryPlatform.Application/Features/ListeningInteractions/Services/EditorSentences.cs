namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

public sealed record EditorGrade(bool Matched, IReadOnlyList<int> MissingMain, bool TooLong);

public static class EditorSentences
{
    public static EditorGrade Grade(IReadOnlyList<bool> isMain, IReadOnlySet<int> kept, int maxKept = 3)
    {
        var missing = Enumerable.Range(0, isMain.Count).Where(index => isMain[index] && !kept.Contains(index)).ToArray();
        var tooLong = kept.Count > maxKept;
        return new(missing.Length == 0 && !tooLong, missing, tooLong);
    }

    public static IReadOnlyList<int> Model(IReadOnlyList<bool> isMain, int maxKept = 3) =>
        Enumerable.Range(0, isMain.Count).Where(index => isMain[index]).Take(maxKept).ToArray();
}
