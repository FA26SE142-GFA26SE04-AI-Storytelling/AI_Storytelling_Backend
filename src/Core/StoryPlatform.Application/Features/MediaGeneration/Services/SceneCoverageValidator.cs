using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

public sealed class SceneCoverageValidator : ISceneCoverageValidator
{
    public IReadOnlyList<ValidatedScene> ValidateAndAssemble(
        string canonicalContent, IReadOnlyList<StoryTextBlock> blocks, IReadOnlyList<SceneSelection> selections)
    {
        if (blocks.Count == 0 || selections.Count == 0) throw new InvalidOperationException("SCENE_COVERAGE_EMPTY");
        var byId = blocks.ToDictionary(x => x.BlockId, StringComparer.Ordinal);
        var expectedIds = blocks.Select(x => x.BlockId).ToArray();
        var consumed = new List<string>(expectedIds.Length);
        var scenes = new List<ValidatedScene>(selections.Count);

        foreach (var selection in selections.OrderBy(x => x.SceneIndex))
        {
            if (selection.SceneIndex != scenes.Count || selection.BlockIds.Count == 0)
                throw new InvalidOperationException("INVALID_SCENE_ORDER");
            var selected = selection.BlockIds.Select(id => byId.TryGetValue(id, out var block)
                ? block : throw new InvalidOperationException("UNKNOWN_SCENE_BLOCK")).ToArray();
            for (var i = 1; i < selected.Length; i++)
                if (selected[i - 1].EndOffset != selected[i].StartOffset)
                    throw new InvalidOperationException("NON_CONTIGUOUS_SCENE_BLOCKS");
            consumed.AddRange(selection.BlockIds);
            var start = selected[0].StartOffset;
            var end = selected[^1].EndOffset;
            var sceneText = canonicalContent[start..end];
            if (!string.Concat(selected.Select(x => x.Text)).Equals(sceneText, StringComparison.Ordinal))
                throw new InvalidOperationException("SCENE_TEXT_NOT_CANONICAL");
            scenes.Add(new ValidatedScene(selection.SceneIndex, start, end, sceneText, selection.Focus));
        }

        if (!consumed.SequenceEqual(expectedIds, StringComparer.Ordinal))
            throw new InvalidOperationException("INCOMPLETE_OR_OVERLAPPING_SCENE_COVERAGE");
        return scenes;
    }
}
