using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Infrastructure.Persistence.Configurations;
using Xunit;

namespace StoryPlatform.IntegrationTests;

public sealed class Luong1SchemaAlignmentModelTests
{
    [Theory]
    [InlineData("ContentReportSlaWarning", 21)]
    [InlineData("ContentReportResolved", 22)]
    public void NotificationType_defines_content_report_values(string name, int expectedValue)
    {
        Assert.True(Enum.TryParse<NotificationType>(name, out var value), name);
        Assert.Equal(expectedValue, (int)value);
    }

    [Fact]
    public void Notification_type_column_fits_every_enum_name()
    {
        using var context = CreateModelOnlyContext();
        var maxLength = context.Model.FindEntityType(typeof(Notification))!
            .FindProperty(nameof(Notification.Type))!.GetMaxLength();

        Assert.NotNull(maxLength);
        Assert.All(Enum.GetNames<NotificationType>(), name => Assert.True(name.Length <= maxLength, name));
    }

    [Fact]
    public void Permission_request_status_column_fits_every_enum_name()
    {
        using var context = CreateModelOnlyContext();
        var maxLength = context.Model.FindEntityType(typeof(SupervisionPermissionRequest))!
            .FindProperty(nameof(SupervisionPermissionRequest.Status))!.GetMaxLength();

        Assert.NotNull(maxLength);
        Assert.All(Enum.GetNames<PermissionRequestStatus>(), name => Assert.True(name.Length <= maxLength, name));
    }

    [Fact]
    public void ReadingSession_has_exactly_one_entry_source_check_constraint()
    {
        using var context = CreateModelOnlyContext();
        var readingSession = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(ReadingSession))!;

        var constraint = Assert.Single(
            readingSession.GetCheckConstraints(),
            value => value.Name == ReadingSessionConfiguration.ExactlyOneEntrySourceConstraintName);
        Assert.Equal(ReadingSessionConfiguration.ExactlyOneEntrySourceSql, constraint.Sql);
        Assert.Contains("\"ChildAccessCredentialId\"", constraint.Sql);
        Assert.Contains("\"SupervisorSessionId\"", constraint.Sql);
    }

    [Fact]
    public void ChildSession_has_exactly_one_entry_source_check_constraint()
    {
        using var context = CreateModelOnlyContext();
        var childSession = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(ChildSession))!;

        var constraint = Assert.Single(
            childSession.GetCheckConstraints(),
            value => value.Name == ChildSessionConfiguration.ExactlyOneEntrySourceConstraintName);
        Assert.Equal(ChildSessionConfiguration.ExactlyOneEntrySourceSql, constraint.Sql);
        Assert.NotNull(childSession.FindProperty(nameof(ChildSession.ChildAccessCredentialId)));
        Assert.NotNull(childSession.FindProperty(nameof(ChildSession.SupervisorSessionId)));
    }

    [Fact]
    public void ChildAccessCredential_unique_profile_index_ignores_soft_deleted_rows()
    {
        using var context = CreateModelOnlyContext();
        var credential = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(ChildAccessCredential))!;

        var index = Assert.Single(credential.GetIndexes(), value =>
            value.IsUnique
            && value.Properties.Single().Name == nameof(ChildAccessCredential.ChildProfileId));
        Assert.Equal("\"IsDeleted\" = false", index.GetFilter());
    }

    private static ApplicationDbContext CreateModelOnlyContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model_only;Password=model_only")
            .Options);
}
