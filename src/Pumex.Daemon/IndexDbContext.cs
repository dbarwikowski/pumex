using Microsoft.Data.Sqlite;

namespace Pumex.Daemon;

/// <summary>
/// Owns the <see cref="SqliteConnection"/>, the concurrency gate, and
/// low-level command helpers shared by all repositories.
/// Registered as a DI singleton; schema is applied via <see cref="IndexSchema"/>.
/// </summary>
public class IndexDbContext : IDisposable
{
    internal readonly SqliteConnection Connection;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public IndexDbContext(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        Connection = new SqliteConnection($"Data Source={dbPath}");
        Connection.Open();
    }

    /// <summary>
    /// Acquires the serialisation gate. All public repository methods call this
    /// before touching SQLite — Microsoft.Data.Sqlite doesn't support concurrent
    /// transactions on a single connection, and WAL throughput is high enough
    /// that the bottleneck is never here.
    /// </summary>
    public async Task<IDisposable> AcquireAsync()
    {
        await _gate.WaitAsync();
        return new Releaser(_gate);
    }

    /// <summary>
    /// Opens a transaction on the shared connection.
    /// <b>Call only while holding the gate</b> (i.e., inside an <see cref="AcquireAsync"/> block).
    /// </summary>
    public SqliteTransaction BeginTransaction() => Connection.BeginTransaction();

    /// <summary>Creates a command with optional named parameters.</summary>
    public SqliteCommand Command(string sql, params (string Name, object Value)[] parameters)
    {
        var cmd = Connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        return cmd;
    }

    /// <summary>Executes a DDL/pragma statement synchronously (no parameters).</summary>
    public void Execute(string sql)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Executes a DML statement asynchronously with named parameters.</summary>
    public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        using var cmd = Command(sql, parameters);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Creates a transactional command with a single <c>$id</c> integer parameter.
    /// Used by bulk-upsert loops to avoid re-preparing per iteration.
    /// </summary>
    public SqliteCommand PrepareById(SqliteTransaction tx, string sql, out SqliteParameter idParam)
    {
        var cmd = Connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        idParam = cmd.Parameters.Add("$id", SqliteType.Integer);
        return cmd;
    }

    public void Dispose() => Connection.Dispose();

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _gate;
        public Releaser(SemaphoreSlim gate) => _gate = gate;
        public void Dispose()
        {
            var g = Interlocked.Exchange(ref _gate, null);
            g?.Release();
        }
    }
}
