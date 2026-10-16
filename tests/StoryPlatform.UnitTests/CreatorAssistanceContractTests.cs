using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class CreatorAssistanceContractTests
{
    [Fact]
    public void RoutesAreCreatorScopedAndAuthorized_WithoutApplyEndpoint()
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(CreatorAssistanceController).Assembly);
        using var provider = services.BuildServiceProvider();
        var actions = provider.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>().Where(x => x.ControllerTypeInfo.AsType() == typeof(CreatorAssistanceController)).ToList();
        Assert.Equal(10, actions.Count);
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(CreatorAssistanceController), typeof(AuthorizeAttribute)));
        Assert.All(actions, a => Assert.StartsWith("api/v1/creator/stories/{storyId:int}/assistance-tasks", a.AttributeRouteInfo!.Template));
        Assert.DoesNotContain(actions, a => a.ActionName.Contains("Apply"));
    }

    [Fact]
    public void ModelKeepsTaskStepReceiptsAndVersionProvenance_WithoutCascadeToStory()
    {
        // Metadata only, never creates, migrates, connects to or seeds any database.
        using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only;Username=unused;Password=unused").Options);
        var task = context.Model.FindEntityType(typeof(StoryAssistanceTask))!;
        var step = context.Model.FindEntityType(typeof(StoryAssistanceStep))!;
        Assert.Equal("story_assistance_tasks", task.GetTableName());
        Assert.Equal("story_assistance_steps", step.GetTableName());
        Assert.Contains(task.GetIndexes(), x => x.IsUnique && x.Properties.Any(p => p.Name == "CreateOperationId"));
        Assert.Contains(step.GetIndexes(), x => x.IsUnique && x.Properties.Any(p => p.Name == "OperationId"));
        Assert.All(task.GetForeignKeys().Concat(step.GetForeignKeys()), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        Assert.True(context.Model.FindEntityType(typeof(StoryVersion))!.FindProperty(nameof(StoryVersion.AssistanceProvenanceJson))!.IsNullable);
    }
}
