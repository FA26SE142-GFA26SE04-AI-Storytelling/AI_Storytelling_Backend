using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace StoryPlatform.Api.Extensions;

public static class DatabaseMigrationLockExtensions
{
    // Postgres advisory lock key (arbitrary but fixed) serializing ApplyPendingMigrations - and
    // the migration-history fix that runs just before it - across any tasks that happen to boot
    // concurrently during a rolling ECS deployment. EF's Migrate() has no built-in protection
    // against two processes racing to apply the same migration at once: whichever loses gets
    // "relation ... already exists" and crashes with an unhandled exception before the app can
    // ever answer /health. Observed repeatedly in production even on task definitions that had
    // booted cleanly before, consistent with an occasional overlap between an old task still
    // draining and (or two) new replacement tasks racing each other, not a one-off fluke.
    private const long MigrationAdvisoryLockKey = 727277001;

    public static void RunMigrationsUnderLock(this IHost host, IConfiguration configuration, Action runMigrations)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            runMigrations();
            return;
        }

        using var lockConnection = new NpgsqlConnection(connectionString);
        lockConnection.Open();

        using (var lockCommand = lockConnection.CreateCommand())
        {
            lockCommand.CommandText = "SELECT pg_advisory_lock(@key);";
            var keyParam = lockCommand.CreateParameter();
            keyParam.ParameterName = "key";
            keyParam.Value = MigrationAdvisoryLockKey;
            lockCommand.Parameters.Add(keyParam);
            lockCommand.ExecuteNonQuery();
        }

        try
        {
            runMigrations();
        }
        finally
        {
            using var unlockCommand = lockConnection.CreateCommand();
            unlockCommand.CommandText = "SELECT pg_advisory_unlock(@key);";
            var keyParam = unlockCommand.CreateParameter();
            keyParam.ParameterName = "key";
            keyParam.Value = MigrationAdvisoryLockKey;
            unlockCommand.Parameters.Add(keyParam);
            unlockCommand.ExecuteNonQuery();
        }
    }
}
