using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StoryPlatform.Api.Extensions;

public static class DatabaseMigrationHistoryFixExtensions
{
    // One-time fix for this project's migration-squash pattern: the single "Init" migration
    // keeps getting regenerated (renamed with a new id + new schema) instead of the project
    // adding genuine incremental migrations, so every regeneration makes Migrate() try to
    // recreate a schema that mostly already exists under the migration's *old* id, crashing
    // with "relation ... already exists" before the app can answer /health.
    //
    // This is the id/delta pair for the THIRD occurrence (2026-09-24, PR #50,
    // "feat(auth): align security parameters and add child session idle timeout"):
    //   20260922113625_Init (PR #45) -> 20260923120512_Init (PR #49, EasyLogin columns)
    //   -> 20260924010248_Init (this one, adds the child_sessions table)
    // Each occurrence's delta is genuinely additive over the previous id, not just a rename, so
    // this can't just mark the new id as applied: it must also create whatever that occurrence
    // actually added (via idempotent IF NOT EXISTS DDL), or that schema would never materialize
    // even though the history table would claim the migration ran.
    //
    // Only runs the delta + insert when the *new* id isn't already recorded, so this is safe to
    // leave in and to run repeatedly - including a no-op on a genuinely fresh database, which
    // has neither id recorded and just lets ApplyPendingMigrations create everything fresh.
    //
    // Split into separate commands (not one parameterized multi-statement string) and logged at
    // Warning (not Information, which production's Logging:LogLevel:Default filters out) - an
    // earlier version combined everything into one command and never proved whether it actually
    // ran in production, since nothing about it ever showed up in CloudWatch even at ERROR level.
    //
    // NOTE for whoever hits this a 4th time: this per-incident patching does not scale. The
    // durable fix is for migration authors to stop deleting/regenerating "Init" and instead run
    // `dotnet ef migrations add <Name>` for schema changes, so Migrate() only ever needs to
    // apply the genuinely new incremental migration.
    private const string CurrentInitMigrationId = "20260924010248_Init";
    private const string CurrentInitProductVersion = "8.0.13"; // Migrations/20260924010248_Init.Designer.cs

    public static void FixMigrationHistoryIfRequested<TContext>(this IHost host, IConfiguration configuration)
        where TContext : DbContext
    {
        if (!configuration.GetValue<bool>("FixMigrationHistory:RunOnStartup"))
        {
            return;
        }

        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseMigrationHistoryFix");

        logger.LogWarning("Migration history fix starting for '{MigrationId}'.", CurrentInitMigrationId);

        try
        {
            var connection = context.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }

            using (var createCommand = connection.CreateCommand())
            {
                createCommand.CommandText = """
                    CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                        "MigrationId" character varying(150) NOT NULL,
                        "ProductVersion" character varying(32) NOT NULL,
                        CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
                    );
                    """;
                createCommand.ExecuteNonQuery();
            }

            bool alreadyApplied;
            using (var checkCommand = connection.CreateCommand())
            {
                checkCommand.CommandText = """
                    SELECT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = @migrationId);
                    """;
                var checkParam = checkCommand.CreateParameter();
                checkParam.ParameterName = "migrationId";
                checkParam.Value = CurrentInitMigrationId;
                checkCommand.Parameters.Add(checkParam);
                alreadyApplied = (bool)checkCommand.ExecuteScalar()!;
            }

            logger.LogWarning(
                "Migration history check for '{MigrationId}': already recorded = {AlreadyApplied}.",
                CurrentInitMigrationId,
                alreadyApplied);

            if (!alreadyApplied)
            {
                // The one real, additive delta this id's regeneration introduced over the
                // previous "20260923120512_Init" id: a new child_sessions table (persisted
                // child sessions bound to JWT claims, for the idle-timeout feature). IF NOT
                // EXISTS makes this safe to run even if a normal (non-crash-looped) migration
                // run already created it.
                using (var deltaCommand = connection.CreateCommand())
                {
                    deltaCommand.CommandText = """
                        CREATE TABLE IF NOT EXISTS child_sessions (
                            "Id" integer GENERATED BY DEFAULT AS IDENTITY,
                            "ChildProfileId" integer NOT NULL,
                            "SessionKey" character varying(64) NOT NULL,
                            "LastActivityAt" timestamp with time zone NOT NULL,
                            "CreatedAt" timestamp with time zone NOT NULL,
                            "UpdatedAt" timestamp with time zone,
                            "IsDeleted" boolean NOT NULL,
                            CONSTRAINT "PK_child_sessions" PRIMARY KEY ("Id"),
                            CONSTRAINT "FK_child_sessions_child_profiles_ChildProfileId"
                                FOREIGN KEY ("ChildProfileId") REFERENCES child_profiles ("Id") ON DELETE RESTRICT
                        );
                        CREATE INDEX IF NOT EXISTS "IX_child_sessions_ChildProfileId" ON child_sessions ("ChildProfileId");
                        CREATE UNIQUE INDEX IF NOT EXISTS "IX_child_sessions_SessionKey" ON child_sessions ("SessionKey");
                        """;
                    deltaCommand.ExecuteNonQuery();
                }

                logger.LogWarning("Applied the child_sessions table delta for '{MigrationId}'.", CurrentInitMigrationId);

                using var insertCommand = connection.CreateCommand();
                insertCommand.CommandText = """
                    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                    VALUES (@migrationId, @productVersion);
                    """;

                var migrationIdParam = insertCommand.CreateParameter();
                migrationIdParam.ParameterName = "migrationId";
                migrationIdParam.Value = CurrentInitMigrationId;
                insertCommand.Parameters.Add(migrationIdParam);

                var productVersionParam = insertCommand.CreateParameter();
                productVersionParam.ParameterName = "productVersion";
                productVersionParam.Value = CurrentInitProductVersion;
                insertCommand.Parameters.Add(productVersionParam);

                var rowsInserted = insertCommand.ExecuteNonQuery();
                logger.LogWarning(
                    "Inserted migration history row for '{MigrationId}': {RowsInserted} row(s).",
                    CurrentInitMigrationId,
                    rowsInserted);
            }
        }
        catch (Exception ex)
        {
            // Must not block startup - if this fails, ApplyPendingMigrations right after
            // surfaces whatever error it always did, which is no worse than before this fix.
            logger.LogError(ex, "Failed to fix migration history for '{MigrationId}'.", CurrentInitMigrationId);
        }
    }
}
