using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class SeedDataAlignmentTests
{
    private sealed record Insert(string File, string Table, string[] Columns, List<Dictionary<string, string>> Rows, bool Derived);

    [Fact]
    public void SeedInserts_MatchCurrentColumnsRequiredValuesEnumsAndForeignKeys()
    {
        // Metadata and SQL text only: this test never connects to or seeds a database.
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only;Username=unused;Password=unused").Options);
        var model = db.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var inserts = ReadInserts();
        Assert.Equal(model.Tables.Select(t => t.Name).OrderBy(x => x), inserts.Select(i => i.Table).Distinct().OrderBy(x => x));
        foreach (var insert in inserts)
        {
            var table = model.Tables.Single(t => t.Name == insert.Table);
            foreach (var name in insert.Columns)
                Assert.NotNull(table.FindColumn(name));
            foreach (var column in table.Columns)
            {
                var properties = column.PropertyMappings.Select(m => m.Property).ToArray();
                var needsValue = !column.IsNullable && properties.All(p => p.ValueGenerated == ValueGenerated.Never &&
                    p.FindAnnotation("Relational:DefaultValue") is null && p.GetDefaultValueSql() is null);
                if (needsValue) Assert.True(insert.Columns.Contains(column.Name), $"{insert.File}: missing {column.Name}");
                foreach (var row in insert.Rows)
                {
                    if (!row.TryGetValue(column.Name, out var raw)) continue;
                    Assert.False(raw == "NULL" && !column.IsNullable, $"{insert.File}: NULL {column.Name}");
                    var literal = StringLiteral(raw);
                    if (literal is null) continue;
                    var type = Nullable.GetUnderlyingType(properties[0].ClrType) ?? properties[0].ClrType;
                    if (type.IsEnum) Assert.True(Enum.GetNames(type).Contains(literal), $"{insert.File}: invalid enum {column.Name}");
                    if (properties[0].GetMaxLength() is int maximum) Assert.True(literal.Length <= maximum, $"{insert.File}: overlong {column.Name}");
                    if (column.StoreType.Contains("json")) using (JsonDocument.Parse(literal)) { }
                }
            }
            foreach (var fk in table.ForeignKeyConstraints)
            foreach (var row in insert.Rows)
            {
                var values = fk.Columns.Select(c => row.GetValueOrDefault(c.Name, "NULL")).ToArray();
                if (values.Contains("NULL")) continue;
                // INSERT ... SELECT rows require SQL execution; this static check
                // verifies their columns, but cannot materialize derived IDs.
                if (inserts.Any(i => i.Table == fk.PrincipalTable.Name && i.Derived)) continue;
                var parentRows = inserts.Where(i => i.Table == fk.PrincipalTable.Name).SelectMany(i => i.Rows);
                Assert.True(parentRows.Any(p => fk.PrincipalColumns.Select((c, index) => p[c.Name] == values[index]).All(x => x)),
                    $"{insert.File}: missing parent for {fk.Name}, Id={row["Id"]}");
            }
        }
    }

    [Fact]
    public void SeedStories_HaveConsistentScopesVersionsAndCurrentAgeBands()
    {
        var inserts = ReadInserts();
        var stories = Rows(inserts, "stories");
        var versions = Rows(inserts, "story_versions");
        var jobs = Rows(inserts, "story_generation_jobs");
        Assert.Contains(stories, s => StringLiteral(s["Scope"]) == "Creator");
        foreach (var story in stories)
        {
            var id = story["Id"];
            var storyVersions = versions.Where(v => v["StoryId"] == id).ToArray();
            Assert.Contains(StringLiteral(story["AgeBand"]), new[] { "6-8", "9-10" });
            Assert.Equal("1", story["AudienceRevision"]);
            Assert.Equal(storyVersions.Max(v => int.Parse(v["VersionNo"])), int.Parse(story["AuthoringLastVersionNo"]));
            Assert.True(storyVersions.Count(v => v["IsCurrent"] == "true") <= 1);
            if (StringLiteral(story["Scope"]) == "Creator")
            {
                Assert.Equal("NULL", story["ChildProfileId"]);
                Assert.Equal("Manual", StringLiteral(story["Source"]));
                Assert.Equal("ReadingMediaOnly", StringLiteral(story["OutputMode"]));
                Assert.Equal("false", story["IsPublished"]);
                Assert.DoesNotContain(jobs, j => j["StoryId"] == id);
                var current = Assert.Single(storyVersions, v => v["IsCurrent"] == "true");
                Assert.Equal(story["Content"], current["Content"]);
                foreach (var version in storyVersions)
                {
                    Assert.Contains(StringLiteral(version["AuthoringOrigin"]), new[] { "Write", "Import", "Restore" });
                    Assert.False(string.IsNullOrWhiteSpace(StringLiteral(version["RightsDeclaration"])));
                    if (version.GetValueOrDefault("BaseVersionId", "NULL") != "NULL")
                        Assert.Contains(storyVersions, v => v["Id"] == version["BaseVersionId"] && int.Parse(v["VersionNo"]) < int.Parse(version["VersionNo"]));
                }
            }
            else Assert.NotEqual("NULL", story["ChildProfileId"]);
        }
        foreach (var row in Rows(inserts, "story_generation_requests"))
        {
            using var snapshot = JsonDocument.Parse(StringLiteral(row["ContextSnapshotJson"])!);
            Assert.Contains(snapshot.RootElement.GetProperty("ageBand").GetString(), new[] { "6-8", "9-10" });
        }
        var assignments = Rows(inserts, "assignments");
        foreach (var recipient in Rows(inserts, "assignment_recipients"))
        {
            var assignment = assignments.Single(a => a["Id"] == recipient["AssignmentId"]);
            Assert.Equal(assignment["Status"], recipient["Status"]);
            Assert.Equal(assignment["ChildProfileId"], recipient["ChildProfileId"]);
        }
    }

    private static Dictionary<string, string>[] Rows(List<Insert> inserts, string table) =>
        inserts.Where(i => i.Table == table).SelectMany(i => i.Rows).ToArray();

    private static List<Insert> ReadInserts()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StoryPlatform.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var result = new List<Insert>();
        foreach (var file in Directory.GetFiles(Path.Combine(root!.FullName, "Database", "Seed"), "*.sql").OrderBy(x => x))
        {
            var sql = File.ReadAllText(file);
            var matches = Regex.Matches(sql, @"INSERT\s+INTO\s+(\w+)\s*\(([^)]*)\)\s*(VALUES|SELECT)\s*([\s\S]*?)ON\s+CONFLICT", RegexOptions.IgnoreCase);
            Assert.Equal(Regex.Matches(sql, @"(?m)^\s*INSERT\s+INTO", RegexOptions.IgnoreCase).Count, matches.Count);
            foreach (Match match in matches)
            {
                var columns = Split(match.Groups[2].Value).Select(s => s.Trim('"')).ToArray();
                var rows = new List<Dictionary<string, string>>();
                var derived = match.Groups[3].Value.Equals("SELECT", StringComparison.OrdinalIgnoreCase);
                foreach (var row in derived ? new List<string>() : Split(match.Groups[4].Value))
                {
                    Assert.StartsWith("(", row);
                    Assert.EndsWith(")", row);
                    var values = Split(row[1..^1]);
                    Assert.Equal(columns.Length, values.Count);
                    rows.Add(columns.Select((c, i) => (c, values[i])).ToDictionary(x => x.c, x => x.Item2));
                }
                result.Add(new Insert(Path.GetFileName(file), match.Groups[1].Value, columns, rows, derived));
            }
        }
        return result;
    }

    private static string? StringLiteral(string raw)
    {
        var match = Regex.Match(raw, @"^'((?:[^']|'')*)'(?:\s*::\s*\w+)?$");
        return match.Success ? match.Groups[1].Value.Replace("''", "'") : null;
    }

    private static List<string> Split(string sql)
    {
        var result = new List<string>();
        var start = 0;
        var depth = 0;
        var quoted = false;
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (c == '\'')
            {
                if (quoted && i + 1 < sql.Length && sql[i + 1] == '\'') { i++; continue; }
                quoted = !quoted;
            }
            if (quoted) continue;
            if (c is '(' or '[') depth++;
            if (c is ')' or ']') depth--;
            if (c == ',' && depth == 0) { result.Add(sql[start..i].Trim()); start = i + 1; }
        }
        var last = sql[start..].Trim();
        if (last.Length > 0) result.Add(last);
        return result;
    }
}
