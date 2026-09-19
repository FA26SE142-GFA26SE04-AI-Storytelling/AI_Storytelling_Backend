using Amazon.CDK;

namespace StoryPlatform.Infra;

public static class Program
{
    public static void Main(string[] args)
    {
        var app = new App();

        Amazon.CDK.Tags.Of(app).Add("Project", "StoryPlatform");

        new StoryPlatformCoreStack(app, "StoryPlatformCoreStack", new StackProps
        {
            Env = new Amazon.CDK.Environment
            {
                Account = "028718096070",
                Region = "ap-southeast-1"
            }
        });

        app.Synth();
    }
}
