using NETworkManager.AI.Persistence;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class SqliteDatabaseTests
{
    [Fact]
    public async Task Initializes_schema_and_version()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();

        using var connection = db.Database.Open();
        connection.Open();

        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        Assert.Equal(SqliteDatabase.CurrentSchemaVersion, Convert.ToInt32(versionCommand.ExecuteScalar()));

        // Tables exist.
        using var tablesCommand = connection.CreateCommand();
        tablesCommand.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('monitoring_results','state_transitions','alerts','alert_occurrences');";
        using var reader = tablesCommand.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Contains("monitoring_results", names);
        Assert.Contains("state_transitions", names);
        Assert.Contains("alerts", names);
        Assert.Contains("alert_occurrences", names);
    }

    [Fact]
    public async Task Idempotent_initialization()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();
        await db.InitializeAsync(); // no throw, version unchanged
    }

    [Fact]
    public async Task Future_schema_version_is_rejected_cleanly()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();

        using (var connection = db.Database.Open())
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 999;";
            command.ExecuteNonQuery();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.InitializeAsync());
    }

    [Fact]
    public async Task Corrupted_file_fails_gracefully()
    {
        using var db = new SqliteTestDb();
        await File.WriteAllTextAsync(Path.Combine(db.Directory, "test.db"), "this is not a sqlite database");

        await Assert.ThrowsAnyAsync<Exception>(() => db.InitializeAsync());
    }
}