using NETworkManager.AI.Persistence;

namespace NETworkManager.AI.Tests.Fakes;

/// <summary>A temp-directory SQLite database for persistence tests; cleaned up on dispose.</summary>
public sealed class SqliteTestDb : IDisposable
{
    public string Directory { get; }

    public SqliteDatabase Database { get; }

    public SqliteTestDb()
    {
        Directory = Path.Combine(Path.GetTempPath(), "twn-persist-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
        Database = new SqliteDatabase(Path.Combine(Directory, "test.db"));
    }

    public Task InitializeAsync() => Database.InitializeAsync();

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}