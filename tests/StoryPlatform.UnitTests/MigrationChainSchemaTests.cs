using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class MigrationChainSchemaTests
{
    [Fact]
    public void MigrationChain_CreatesRequiredColumnsBeforeDataUpdates_AndCoversCurrentModel()
    {
        // Reconstruct the schema from migration operations without opening a database.
        // Unlike snapshot comparison, this catches changes omitted from historical Up().
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only;Username=unused;Password=unused").Options);
        var assembly = db.GetService<IMigrationsAssembly>();
        var tables = new Dictionary<string, HashSet<string>>();
        foreach (var entry in assembly.Migrations.OrderBy(x => x.Key))
        {
            var migration = assembly.CreateMigration(entry.Value, db.Database.ProviderName!);
            foreach (var operation in migration.UpOperations)
            {
                switch (operation)
                {
                    case CreateTableOperation create:
                        Assert.True(tables.TryAdd(create.Name, create.Columns.Select(x => x.Name).ToHashSet()),
                            $"{entry.Key} creates an existing table: {create.Name}");
                        break;
                    case AddColumnOperation add:
                        Assert.True(tables[add.Table].Add(add.Name), $"Duplicate column: {add.Table}.{add.Name}");
                        break;
                    case DropColumnOperation drop:
                        Assert.True(tables[drop.Table].Remove(drop.Name));
                        break;
                    case RenameColumnOperation rename:
                        Assert.True(tables[rename.Table].Remove(rename.Name));
                        Assert.True(tables[rename.Table].Add(rename.NewName));
                        break;
                    case DropTableOperation dropTable:
                        Assert.True(tables.Remove(dropTable.Name));
                        break;
                    case SqlOperation sql:
                        foreach (Match match in Regex.Matches(sql.Sql,
                            "ALTER TABLE (?:public\\.)?(\\w+) ADD COLUMN IF NOT EXISTS \"([^\"]+)\"", RegexOptions.IgnoreCase))
                            tables[match.Groups[1].Value].Add(match.Groups[2].Value);
                        foreach (Match match in Regex.Matches(sql.Sql,
                            "UPDATE (\\w+) SET \"([^\"]+)\"", RegexOptions.IgnoreCase))
                            Assert.True(tables[match.Groups[1].Value].Contains(match.Groups[2].Value),
                                $"{entry.Key} updates a missing column: {match.Groups[1].Value}.{match.Groups[2].Value}");
                        break;
                }
            }
        }

        foreach (var table in db.GetService<IDesignTimeModel>().Model.GetRelationalModel().Tables)
        {
            Assert.True(tables.ContainsKey(table.Name), $"Missing table: {table.Name}");
            foreach (var column in table.Columns)
                Assert.True(tables[table.Name].Contains(column.Name), $"Missing column: {table.Name}.{column.Name}");
        }
    }
}
