using TypedPond.Core;
using Xunit;

namespace TypedPond.Tests;

/// <summary>
/// Tests for <see cref="StepStore"/> against a real SQLite database backed by a
/// temporary file. Each test gets its own fresh database file, and the file is
/// deleted when the test finishes via <see cref="IDisposable"/>.
/// </summary>
public sealed class StepStoreTests : IDisposable
{
    private readonly string _databasePath;

    public StepStoreTests()
    {
        // Unique temp file per test instance (xUnit creates one instance per test).
        _databasePath = Path.Combine(
            Path.GetTempPath(),
            $"typedpond-test-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        // Microsoft.Data.Sqlite pools connections by default; clear the pool so the
        // OS file handle is released before we delete the file.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        TryDelete(_databasePath);
        // SQLite may create sidecar files depending on journal mode.
        TryDelete(_databasePath + "-wal");
        TryDelete(_databasePath + "-shm");
        TryDelete(_databasePath + "-journal");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; ignore if the file is still locked.
        }
    }

    private async Task<StepStore> CreateInitializedStoreAsync()
    {
        var store = new StepStore(_databasePath);
        await store.InitializeAsync();
        return store;
    }

    [Fact]
    public void Constructor_EmptyPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => new StepStore(string.Empty));
        Assert.Throws<ArgumentException>(() => new StepStore("   "));
    }

    [Fact]
    public async Task Initialize_CreatesTable_SubsequentOperationsWork()
    {
        var store = await CreateInitializedStoreAsync();

        // If the table were missing, this Get would throw a SqliteException.
        int? result = await store.GetStepsAsync("2026-07-14");

        Assert.Null(result);
    }

    [Fact]
    public async Task Initialize_IsIdempotent()
    {
        var store = new StepStore(_databasePath);

        await store.InitializeAsync();
        await store.InitializeAsync(); // CREATE TABLE IF NOT EXISTS -> no throw

        await store.UpsertStepsAsync("2026-07-14", 5000);
        Assert.Equal(5000, await store.GetStepsAsync("2026-07-14"));
    }

    [Fact]
    public async Task UpsertThenGet_ReturnsCount()
    {
        var store = await CreateInitializedStoreAsync();

        await store.UpsertStepsAsync("2026-07-14", 8123);

        Assert.Equal(8123, await store.GetStepsAsync("2026-07-14"));
    }

    [Fact]
    public async Task UpsertTwiceSameDate_UpdatesCount_NoDuplicate()
    {
        var store = await CreateInitializedStoreAsync();

        await store.UpsertStepsAsync("2026-07-14", 3000);
        await store.UpsertStepsAsync("2026-07-14", 7500);

        // The second upsert must overwrite, not insert a second row.
        Assert.Equal(7500, await store.GetStepsAsync("2026-07-14"));
    }

    [Fact]
    public async Task Get_NoRecord_ReturnsNull()
    {
        var store = await CreateInitializedStoreAsync();

        Assert.Null(await store.GetStepsAsync("2099-01-01"));
    }

    [Fact]
    public async Task MultipleDates_StoredAndRetrievedIndependently()
    {
        var store = await CreateInitializedStoreAsync();

        await store.UpsertStepsAsync("2026-07-12", 1000);
        await store.UpsertStepsAsync("2026-07-13", 2000);
        await store.UpsertStepsAsync("2026-07-14", 3000);

        Assert.Equal(1000, await store.GetStepsAsync("2026-07-12"));
        Assert.Equal(2000, await store.GetStepsAsync("2026-07-13"));
        Assert.Equal(3000, await store.GetStepsAsync("2026-07-14"));
        Assert.Null(await store.GetStepsAsync("2026-07-15"));
    }

    [Fact]
    public async Task Upsert_ZeroCount_IsStoredAndDistinctFromNull()
    {
        var store = await CreateInitializedStoreAsync();

        await store.UpsertStepsAsync("2026-07-14", 0);

        // A stored 0 must not be confused with "no record" (null).
        int? result = await store.GetStepsAsync("2026-07-14");
        Assert.Equal(0, result);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Data_PersistsAcrossStoreInstances_SameFile()
    {
        var writer = await CreateInitializedStoreAsync();
        await writer.UpsertStepsAsync("2026-07-14", 6000);

        // A new StepStore pointed at the same file must see the persisted data.
        var reader = new StepStore(_databasePath);
        Assert.Equal(6000, await reader.GetStepsAsync("2026-07-14"));
    }
}
