using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using StoryPlatform.Application.Features.CreatorAssistance.DTOs;
using StoryPlatform.Contracts.AI.CreatorAssistance;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class SeedDataAlignmentTests
{
    [Fact]
    public void SeedExplicitIds_AreUniqueAcrossAllInsertsForEachTable()
    {
        // ON CONFLICT DO NOTHING must not silently discard another feature's fixture.
        foreach (var table in ReadInserts().GroupBy(insert => insert.Table))
        {
            var duplicates = table.SelectMany(insert => insert.Rows.Select(row =>
                    (Id: row.GetValueOrDefault("Id", row.GetValueOrDefault("StoryVersionId", "derived")), File: insert.File)))
                .GroupBy(row => row.Id)
                .Where(group => group.Count() > 1)
                .Select(group => $"Id={group.Key} in [{string.Join(", ", group.Select(row => row.File))}]");
            Assert.True(!duplicates.Any(), $"{table.Key}: duplicate seed IDs: {string.Join("; ", duplicates)}");
        }
    }

    [Fact]
    public void SeedParentOperations_ReferencePrivateParentStoryAndMatchingVersion()
    {
        var inserts = ReadInserts();
        foreach (var operation in Rows(inserts, "parent_story_operations"))
        {
            var story = Assert.Single(Rows(inserts, "stories"), row => row["Id"] == operation["StoryId"]);
            var version = Assert.Single(Rows(inserts, "story_versions"), row => row["Id"] == operation["StoryVersionId"]);
            Assert.Equal("Parent", StringLiteral(story["Scope"]));
            Assert.Equal("Manual", StringLiteral(story["Source"]));
            Assert.Equal("false", story["IsPublished"]);
            Assert.Equal(story["AuthorUserId"], operation["ActorUserId"]);
            Assert.Equal(story["ChildProfileId"], operation["ChildProfileId"]);
            Assert.Equal(story["Id"], version["StoryId"]);
            Assert.Equal(story["Content"], version["Content"]);
            Assert.Equal("true", version["IsCurrent"]);
            using var result = JsonDocument.Parse(StringLiteral(operation["ResultJson"])!);
            Assert.Equal(int.Parse(version["Id"]), result.RootElement.GetProperty("storyVersionId").GetInt32());
            Assert.False(result.RootElement.GetProperty("canPrepare").GetBoolean());
        }
    }

    [Fact]
    public void SeedManifest_has_one_target_table_per_file_matching_runner_contract()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StoryPlatform.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var files = Directory.GetFiles(Path.Combine(root.FullName, "Database", "Seed"), "*.sql").OrderBy(x => x).ToArray();
        for (var index = 0; index < files.Length; index++)
        {
            var name = Path.GetFileName(files[index]);
            var match = Regex.Match(name, @"^(?<order>\d{2})_(?<table>[a-z][a-z0-9_]*)\.sql$");
            Assert.True(match.Success, name);
            Assert.Equal(index + 1, int.Parse(match.Groups["order"].Value));
            // Same pattern as Seed-Database.ps1:Get-SeedTableName, including quoted identifiers.
            var inserts = Regex.Matches(File.ReadAllText(files[index]),
                "(?im)^\\s*INSERT\\s+INTO\\s+(?:\"(?<quoted>[a-z][a-z0-9_]*)\"|(?<plain>[a-z][a-z0-9_]*))");
            var tables = inserts.Select(x => x.Groups["quoted"].Success ? x.Groups["quoted"].Value : x.Groups["plain"].Value)
                .Distinct(StringComparer.Ordinal).ToArray();
            Assert.Equal(match.Groups["table"].Value, Assert.Single(tables));
        }
    }

    [Fact]
    public void SeedAssistance_ClosedTaskKeepsSourceContextAndReplayReceiptsConsistent()
    {
        var inserts = ReadInserts();
        var task = Assert.Single(Rows(inserts, "story_assistance_tasks"));
        var step = Assert.Single(Rows(inserts, "story_assistance_steps"));
        var sourceStory = Assert.Single(Rows(inserts, "stories"), s => s["Id"] == task["StoryId"]);
        var sourceVersion = Assert.Single(Rows(inserts, "story_versions"), v => v["Id"] == task["SourceVersionId"]);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        T Read<T>(Dictionary<string, string> row, string key) => JsonSerializer.Deserialize<T>(StringLiteral(row[key])!, json)!;
        string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, json))));
        var context = Read<CreateAssistanceTaskRequest>(task, "ContextJson");
        var creation = Read<AssistanceTaskDto>(task, "CreateResponseJson");
        var command = Read<AssistanceCommand>(step, "InputJson");
        var receipt = Read<AssistanceCommandResult>(step, "ResponseJson");

        Assert.Equal("Creator", StringLiteral(sourceStory["Scope"]));
        Assert.Equal(sourceStory["AuthorUserId"], task["OwnerUserId"]);
        Assert.Equal(task["StoryId"], sourceVersion["StoryId"]);
        Assert.Equal("true", sourceVersion["IsCurrent"]);
        Assert.Equal(int.Parse(sourceVersion["Id"]), context.BaseVersionId);
        Assert.Equal(StringLiteral(sourceVersion["Title"]), context.EditorSnapshot.Title);
        Assert.Equal(StringLiteral(sourceVersion["Content"]), context.EditorSnapshot.Content);
        var revisionExpression = Regex.Match(sourceStory["AuthoringRevisionToken"], @"^md5\('([^']+)'\)$");
        Assert.True(revisionExpression.Success);
        var revision = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(revisionExpression.Groups[1].Value))).ToLowerInvariant();
        Assert.Equal(revision, context.RevisionToken);
        Assert.Equal(revision, StringLiteral(task["SourceRevisionToken"]));
        Assert.Equal(context.OperationId, StringLiteral(task["CreateOperationId"]));
        Assert.Equal(Hash(context), StringLiteral(task["CreateRequestHash"]));
        var snapshotHash = Hash(new[] { context.EditorSnapshot.Title, context.EditorSnapshot.Content });
        Assert.Equal(snapshotHash, StringLiteral(task["SnapshotHash"]));
        AssistanceValidation.ValidateTarget(context.EntryPoint, context.EditorSnapshot, context.Target);
        AssistanceValidation.ValidateConstraints(context.Constraints);

        Assert.Equal(int.Parse(task["Id"]), creation.TaskId);
        Assert.Equal(context.EditorSnapshot, creation.Snapshot);
        Assert.Equal(context.Target, creation.Target);
        Assert.Equal("Open", creation.Status); // A create replay returns its original historical receipt.
        Assert.Equal(command.ExpectedTaskRevision, creation.TaskRevision);
        Assert.Equal(snapshotHash, creation.SnapshotHash);
        Assert.Equal("Closed", StringLiteral(task["Status"]));
        Assert.Equal("NULL", task.GetValueOrDefault("ActiveOperationId", "NULL"));
        Assert.Equal("NULL", task.GetValueOrDefault("LeaseExpiresAt", "NULL"));
        Assert.Equal(task["Id"], step["TaskId"]);
        Assert.Equal(task["LastSequence"], step["Sequence"]);
        Assert.Equal(Hash(new { kind = "Close", request = (object)command }), StringLiteral(step["RequestHash"]));
        Assert.Equal(command.OperationId, StringLiteral(step["OperationId"]));
        Assert.Equal(command.ExpectedTaskRevision + 1, int.Parse(task["TaskRevision"]));
        Assert.Equal(int.Parse(task["TaskRevision"]), receipt.TaskRevision);
        Assert.Equal(receipt.TaskRevision, receipt.Step.ResultingTaskRevision);
        Assert.Equal(int.Parse(step["Id"]), receipt.StepId);
        Assert.Equal(creation.TaskId, receipt.TaskId);
        Assert.Equal("Closed", receipt.Status);
        Assert.False(receipt.IsRunning);
        Assert.Equal("Close", receipt.Step.Kind);
        Assert.Equal("Succeeded", receipt.Step.Status);
        Assert.Null(receipt.Step.Result);
        Assert.Equal("NULL", step["OutputJson"]);
        Assert.Equal("NULL", step.GetValueOrDefault("Provider", "NULL"));
        Assert.Equal(snapshotHash, receipt.SnapshotHash);
    }
    private sealed record Insert(string File, string Table, string[] Columns, List<Dictionary<string, string>> Rows, bool Derived);

    [Fact]
    public void SeedInserts_MatchCurrentColumnsRequiredValuesEnumsAndForeignKeys()
    {
        // Metadata and SQL text only: this test never connects to or seeds a database.
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only;Username=unused;Password=unused").Options);
        var model = db.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var inserts = ReadInserts();
        var modelTables = model.Tables.Select(t => t.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var seedTables = inserts.Select(i => i.Table).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var missingInserts = modelTables.Except(seedTables, StringComparer.Ordinal).ToArray();
        var extraInserts = seedTables.Except(modelTables, StringComparer.Ordinal).ToArray();
        Assert.True(modelTables.SequenceEqual(seedTables, StringComparer.Ordinal),
            $"Seed coverage mismatch. Missing seed INSERTs: [{string.Join(", ", missingInserts)}]. " +
            $"Seed INSERTs for unknown tables: [{string.Join(", ", extraInserts)}]. " +
            $"Model tables ({modelTables.Length}): [{string.Join(", ", modelTables)}]. " +
            $"Seed tables ({seedTables.Length}): [{string.Join(", ", seedTables)}].");
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
                    $"{insert.File}: missing parent for {fk.Name}, Id={row.GetValueOrDefault("Id", row.GetValueOrDefault("StoryVersionId", "derived"))}");
            }
        }
    }

    [Fact]
    public void SeedStories_HaveConsistentScopesVersionsAndCurrentAgeBands()
    {
        var inserts = ReadInserts();
        var stories = Rows(inserts, "stories");
        var versions = VersionRows(inserts);
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
                if (story["IsPublished"] == "true")
                    Assert.NotEqual("NULL", story["ChildVisibleAt"]);
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

    [Fact]
    public void SeedPublicStoryReview_CoversSubmitResubmitQueueHistoryAndComparisonScenarios()
    {
        var inserts = ReadInserts();
        var users = Rows(inserts, "user_accounts");
        var stories = Rows(inserts, "stories");
        var versions = VersionRows(inserts);
        var items = Rows(inserts, "moderation_items");

        var creator = users.Single(user => user["Id"] == "13");
        Assert.Equal("ContentCreator", StringLiteral(creator["Role"]));
        Assert.Equal("Active", StringLiteral(creator["Status"]));

        // First submit/withdraw: ready creator story, current version defaults to ReviewStatus=None,
        // and there is no pre-existing open PublicStoryReview item.
        var firstSubmitStory = stories.Single(story => story["Id"] == "11");
        var firstSubmitVersion = versions.Single(version => version["Id"] == "12");
        Assert.Equal("Creator", StringLiteral(firstSubmitStory["Scope"]));
        Assert.Equal("Ready", StringLiteral(firstSubmitStory["Status"]));
        Assert.Equal("13", firstSubmitStory["AuthorUserId"]);
        Assert.Equal("true", firstSubmitVersion["IsCurrent"]);
        Assert.Equal("None", StringLiteral(firstSubmitVersion["ReviewStatus"]));
        Assert.DoesNotContain(items, item => item.GetValueOrDefault("ItemType") == "'PublicStoryReview'"
                                              && item.GetValueOrDefault("StoryId") == "11");

        // Creator resubmit: public v1 -> returned v2 -> genuinely changed current v3 (not yet queued).
        AssertVersion(14, 13, "Approved", isCurrent: false);
        AssertVersion(15, 13, "ChangesRequested", isCurrent: false);
        AssertVersion(16, 13, "None", isCurrent: true);
        var returnedForCreator = items.Single(item => item["Id"] == "18");
        Assert.Equal("ChangesRequested", StringLiteral(returnedForCreator["Status"]));
        Assert.Equal("17", returnedForCreator["PreviousItemId"]);
        Assert.Equal("true", returnedForCreator["IsResubmission"]);
        Assert.DoesNotContain(items, item => item.GetValueOrDefault("StoryId") == "13"
                                              && StringLiteral(item.GetValueOrDefault("Status", ""))
                                                  is "Pending" or "Claimed");

        // Moderator path: a recent pending resubmission linked to two completed review rounds.
        AssertVersion(17, 14, "Approved", isCurrent: false);
        AssertVersion(18, 14, "ChangesRequested", isCurrent: false);
        AssertVersion(19, 14, "Pending", isCurrent: true);
        var pendingResubmission = items.Single(item => item["Id"] == "21");
        Assert.Equal("PublicStoryReview", StringLiteral(pendingResubmission["ItemType"]));
        Assert.Equal("Pending", StringLiteral(pendingResubmission["Status"]));
        Assert.Equal("20", pendingResubmission["PreviousItemId"]);
        Assert.Equal("true", pendingResubmission["IsResubmission"]);
        Assert.Contains("INTERVAL '1 hour'", pendingResubmission["CreatedAt"]);
        Assert.Equal("11", items.Single(item => item["Id"] == "20")["DecidedByUserId"]);

        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StoryPlatform.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        var scenesSql = File.ReadAllText(Path.Combine(root!.FullName, "Database", "Seed", "15_story_scenes.sql"));
        var mediaSql = File.ReadAllText(Path.Combine(root.FullName, "Database", "Seed", "20_media_assets.sql"));
        Assert.Contains("1, 3, 6, 10, 12, 14, 15, 16, 17, 18, 19", scenesSql);
        foreach (var versionId in new[] { 12, 14, 15, 16, 17, 18, 19 })
        {
            Assert.Contains($", {versionId}, {versionId}, NULL, 'Illustration'", mediaSql);
            Assert.Contains($", {versionId}, {versionId}, {versionId}, 'TtsAudio'", mediaSql);
        }

        void AssertVersion(int id, int storyId, string reviewStatus, bool isCurrent)
        {
            var version = versions.Single(candidate => candidate["Id"] == id.ToString());
            Assert.Equal(storyId.ToString(), version["StoryId"]);
            Assert.Equal(reviewStatus, StringLiteral(version["ReviewStatus"]));
            Assert.Equal(isCurrent.ToString().ToLowerInvariant(), version["IsCurrent"]);
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

    private static Dictionary<string, string>[] VersionRows(List<Insert> inserts)
    {
        var versions = Rows(inserts, "story_versions");
        foreach (var version in versions)
        foreach (var table in new[] { "story_version_authoring_metadata", "story_version_moderation_states", "story_version_media_states" })
        {
            var metadata = Assert.Single(Rows(inserts, table), row => row["StoryVersionId"] == version["Id"]);
            foreach (var pair in metadata.Where(x => x.Key is not ("Id" or "StoryVersionId" or "StoryId"))) version[pair.Key] = pair.Value;
        }
        return versions.ToArray();
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
