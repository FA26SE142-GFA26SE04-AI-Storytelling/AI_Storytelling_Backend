using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

public sealed class SceneSpecificationBuilder : ISceneSpecificationBuilder
{
    public SceneSpecification Build(int storyVersionId, int storySceneId, int sceneIndex, string sceneText,
        string? visualDescription, string mediaContextJson)
    {
        if (string.IsNullOrEmpty(sceneText)) throw new InvalidOperationException("EMPTY_SCENE_TEXT");
        return new SceneSpecification(
            storyVersionId, storySceneId, sceneIndex, sceneText, visualDescription, mediaContextJson,
            string.IsNullOrWhiteSpace(visualDescription) ? Array.Empty<string>() : new[] { visualDescription },
            new[] { "canonical story facts", "character identity", "timeline", "safety constraints" });
    }
}
