using Microsoft.Data.Sqlite;

namespace TypedPond.Core;

/// <summary>
/// SQLite-backed store for daily step counts.
/// Schema: steps(date TEXT PRIMARY KEY, count INTEGER, updated_at TEXT)
/// </summary>
public class StepStore
{
    private readonly string _connectionString;

    /// <param name="databasePath">Absolute or relative path to the SQLite database file.</param>
    public StepStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path must not be empty.", nameof(databasePath));
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
        }.ToString();
    }

    /// <summary>Creates the steps table if it does not already exist.</summary>
    public async Task InitializeAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS steps (
                date TEXT PRIMARY KEY,
                count INTEGER,
                updated_at TEXT
            );
            """;
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Inserts or updates the step count for a date, setting updated_at to UTC now.
    /// </summary>
    /// <param name="date">Date key, e.g. "2026-07-14".</param>
    /// <param name="count">Step count for that date.</param>
    public async Task UpsertStepsAsync(string date, int count)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO steps (date, count, updated_at)
            VALUES ($date, $count, $updatedAt)
            ON CONFLICT(date) DO UPDATE SET
                count = excluded.count,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$date", date);
        command.Parameters.AddWithValue("$count", count);
        command.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Returns the step count for a date, or null if there is no record.
    /// </summary>
    public async Task<int?> GetStepsAsync(string date)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count FROM steps WHERE date = $date;";
        command.Parameters.AddWithValue("$date", date);

        object? result = await command.ExecuteScalarAsync();
        if (result is null || result is DBNull)
        {
            return null;
        }

        return Convert.ToInt32(result);
    }
}
