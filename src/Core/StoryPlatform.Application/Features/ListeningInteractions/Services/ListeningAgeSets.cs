using StoryPlatform.Domain.Enums;
using StoryPlatform.Contracts.AI.Models;

namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

/// <summary>Independent interaction sets, served only to children of the exact target age.</summary>
public static class ListeningAgeSets
{
    public static IReadOnlyList<int> Built => ListeningAgeCatalog.BuiltAges;

    public static bool HasOwnSet(int? age) => age is int value && Built.Contains(value);

    public static IReadOnlyList<int> TargetAgesFor(AgeBand band) =>
        AgesIn(band).Where(Built.Contains).ToArray();

    public static int AgeFor(int? childAge, AgeBand band) => childAge ?? AgesIn(band)[0];

    private static int[] AgesIn(AgeBand band) => band == AgeBand.Age_6_8 ? [6, 7, 8] : [9, 10];
}
