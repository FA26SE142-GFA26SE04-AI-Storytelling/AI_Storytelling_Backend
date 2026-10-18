using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.Moderation;

/// <summary>Builds a deterministic, non-AI comparison report for two public story versions.</summary>
public static class PublicVersionComparer
{
    public static PublicVersionComparisonDto Compare(PublicVersionSnapshot before, PublicVersionSnapshot current)
    {
        var result = new PublicVersionComparisonDto
        {
            BaseVersionId = before.Version.Id,
            BaseVersionNo = before.Version.VersionNo,
            CurrentVersionId = current.Version.Id,
            CurrentVersionNo = current.Version.VersionNo,
            IsIdentical = before.Fingerprint() == current.Fingerprint(),
            TextDiff = TextDiff.Compare(before.Version.Content, current.Version.Content)
                .Select(entry => new TextDiffEntryDto { Type = entry.Type, Before = entry.Before, After = entry.After })
                .ToList(),
            BaseMetrics = BuildMetrics(before.Version),
            CurrentMetrics = BuildMetrics(current.Version)
        };

        AddChangedField(result, "title", before.Version.Title, current.Version.Title);
        AddChangedField(result, "lesson", before.Version.Lesson, current.Version.Lesson);
        AddChangedField(result, "outlineOpening", before.Version.OutlineOpening, current.Version.OutlineOpening);
        AddChangedField(result, "outlineDevelopment", before.Version.OutlineDevelopment, current.Version.OutlineDevelopment);
        AddChangedField(result, "outlineEnding", before.Version.OutlineEnding, current.Version.OutlineEnding);

        CompareScenes(result, before, current);
        DiffKeyed(result.ArtifactChanges, "quiz", before.Quizzes, current.Quizzes,
            quiz => quiz.Question.Trim(), quiz => $"{quiz.Type}|{quiz.CorrectAnswer}|{quiz.Choices}");
        DiffKeyed(result.ArtifactChanges, "vocabulary", before.Vocabulary, current.Vocabulary,
            word => word.Term.Trim(), word => word.Definition);
        DiffKeyed(result.ArtifactChanges, "discussion", before.Discussions, current.Discussions,
            question => question.Question.Trim(), question => question.IsMoralLesson.ToString());

        return result;
    }

    private static PublicVersionMetricsDto BuildMetrics(StoryVersion version)
    {
        var words = (version.Content ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var sentences = TextDiff.SplitUnits(version.Content).Count;
        return new PublicVersionMetricsDto
        {
            WordCount = words,
            SentenceCount = sentences,
            AverageSentenceLength = sentences == 0 ? 0 : Math.Round((double)words / sentences, 2),
            ReadabilityFkgl = version.ReadabilityFkgl,
            ReadabilityFre = version.ReadabilityFre
        };
    }

    private static void AddChangedField(PublicVersionComparisonDto result, string name, string? before, string? after)
    {
        if (!string.Equals(before ?? string.Empty, after ?? string.Empty, StringComparison.Ordinal))
        {
            result.ChangedFields.Add(name);
        }
    }

    private static void CompareScenes(
        PublicVersionComparisonDto result, PublicVersionSnapshot before, PublicVersionSnapshot current)
    {
        var beforeScenes = before.Scenes.GroupBy(scene => scene.SceneIndex)
            .ToDictionary(group => group.Key, group => group.First());
        var currentScenes = current.Scenes.GroupBy(scene => scene.SceneIndex)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var index in beforeScenes.Keys.Union(currentScenes.Keys).OrderBy(index => index))
        {
            var inBefore = beforeScenes.TryGetValue(index, out var oldScene);
            var inCurrent = currentScenes.TryGetValue(index, out var newScene);
            if (!inBefore)
            {
                result.SceneChanges.Add(new SceneChangeDto
                {
                    SceneIndex = index, Change = "added", TextChanged = true, ImageChanged = true, AudioChanged = true
                });
            }
            else if (!inCurrent)
            {
                result.SceneChanges.Add(new SceneChangeDto
                {
                    SceneIndex = index, Change = "removed", TextChanged = true, ImageChanged = true, AudioChanged = true
                });
            }
            else
            {
                var textChanged = oldScene!.SceneText != newScene!.SceneText;
                var imageChanged = before.ImageKey(oldScene.Id) != current.ImageKey(newScene.Id);
                var audioChanged = before.AudioKey(oldScene.Id) != current.AudioKey(newScene.Id);
                if (textChanged || imageChanged || audioChanged)
                {
                    result.SceneChanges.Add(new SceneChangeDto
                    {
                        SceneIndex = index,
                        Change = "modified",
                        TextChanged = textChanged,
                        ImageChanged = imageChanged,
                        AudioChanged = audioChanged
                    });
                }
            }
        }
    }

    private static void DiffKeyed<T>(
        List<ArtifactChangeDto> changes,
        string kind,
        IEnumerable<T> before,
        IEnumerable<T> after,
        Func<T, string> key,
        Func<T, string> value)
    {
        var oldItems = before.GroupBy(key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => value(group.First()), StringComparer.OrdinalIgnoreCase);
        var newItems = after.GroupBy(key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => value(group.First()), StringComparer.OrdinalIgnoreCase);

        foreach (var removedKey in oldItems.Keys.Where(itemKey => !newItems.ContainsKey(itemKey)))
        {
            changes.Add(new ArtifactChangeDto { Kind = kind, Change = "removed", Key = removedKey });
        }

        foreach (var addedKey in newItems.Keys.Where(itemKey => !oldItems.ContainsKey(itemKey)))
        {
            changes.Add(new ArtifactChangeDto { Kind = kind, Change = "added", Key = addedKey });
        }

        foreach (var sharedKey in newItems.Keys.Where(itemKey =>
                     oldItems.ContainsKey(itemKey) && oldItems[itemKey] != newItems[itemKey]))
        {
            changes.Add(new ArtifactChangeDto { Kind = kind, Change = "modified", Key = sharedKey });
        }
    }
}
