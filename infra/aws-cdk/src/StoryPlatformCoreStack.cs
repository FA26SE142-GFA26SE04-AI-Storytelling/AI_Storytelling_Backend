using Amazon.CDK;
using Constructs;

namespace StoryPlatform.Infra;

public sealed class StoryPlatformCoreStack : Stack
{
    public StoryPlatformCoreStack(Construct scope, string id, IStackProps? props = null)
        : base(scope, id, props)
    {
    }
}
