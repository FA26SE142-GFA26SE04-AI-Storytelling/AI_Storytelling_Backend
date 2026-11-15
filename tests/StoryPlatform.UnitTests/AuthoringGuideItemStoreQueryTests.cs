using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class AuthoringGuideItemStoreQueryTests
{
    [Fact]
    public async Task Loading_guides_translates_placeholder_filter_with_Npgsql_and_keeps_only_matching_live_items()
    {
        // Translate the actual store predicate without connecting to a database.
        // Compiling only against an in-memory fake misses span-based Contains failures.
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=query_translation_only;Username=fixture;Password=fixture").Options);
        var rows = new[]
        {
            new AuthoringGuideItem { Id = 12, CatalogVersionId = 500, GuideKey = "b", Order = 2 },
            new AuthoringGuideItem { Id = 11, CatalogVersionId = 500, GuideKey = "a", Order = 1 },
        };
        var blanks = new[]
        {
            new AuthoringGuidePlaceholder { ItemId = 11, Key = "place", Order = 2 },
            new AuthoringGuidePlaceholder { ItemId = 11, Key = "person", Order = 1 },
            new AuthoringGuidePlaceholder { ItemId = 12, Key = "activity", Order = 1 },
            new AuthoringGuidePlaceholder { ItemId = 13, Key = "other-version" },
            new AuthoringGuidePlaceholder { ItemId = 11, Key = "deleted", IsDeleted = true },
        };
        var items = new Mock<IGenericRepository<AuthoringGuideItem>>();
        items.Setup(x => x.FindAsync(It.IsAny<Expression<Func<AuthoringGuideItem, bool>>>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        var placeholders = new Mock<IGenericRepository<AuthoringGuidePlaceholder>>();
        string? sql = null;
        placeholders.Setup(x => x.FindAsync(It.IsAny<Expression<Func<AuthoringGuidePlaceholder, bool>>>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns((Expression<Func<AuthoringGuidePlaceholder, bool>> filter, string? _, CancellationToken __) =>
            {
                sql = db.Set<AuthoringGuidePlaceholder>().Where(filter).ToQueryString();
                return Task.FromResult<IReadOnlyList<AuthoringGuidePlaceholder>>(blanks.Where(filter.Compile()).ToList());
            });
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Repository<AuthoringGuideItem>()).Returns(items.Object);
        uow.Setup(x => x.Repository<AuthoringGuidePlaceholder>()).Returns(placeholders.Object);

        var guides = await new AuthoringGuideItemStore(uow.Object).LoadAsync(500, "Parent", default);

        Assert.False(string.IsNullOrWhiteSpace(sql));
        Assert.Equal(["a", "b"], guides.Select(x => x.Id));
        Assert.Equal(["person", "place"], guides[0].PlaceholderDefinitions.Select(x => x.Key));
        Assert.Equal(["activity"], guides[1].PlaceholderDefinitions.Select(x => x.Key));
    }
}
