using System.Reflection;
using Microsoft.Data.Sqlite;

namespace VGList.Data;

/// <summary>
/// SQLite 连接与顺序迁移管理器：
/// 启用 WAL / synchronous=NORMAL / foreign_keys=ON；自动处理损坏库改名备份重建；高版本库只读保护。
/// </summary>
public sealed class SqliteDatabase : IDisposable
{
    public const int LatestSchemaVersion = 4;

    private readonly SqliteConnection _connection;
    private bool _disposed;

    private SqliteDatabase(
        string filePath,
        SqliteConnection connection,
        bool recoveredFromCorruption,
        string? backupCorruptPath,
        bool isReadOnlyFutureVersion)
    {
        FilePath = filePath;
        _connection = connection;
        RecoveredFromCorruption = recoveredFromCorruption;
        BackupCorruptPath = backupCorruptPath;
        IsReadOnlyFutureVersion = isReadOnlyFutureVersion;
    }

    public string FilePath { get; }

    public bool RecoveredFromCorruption { get; }

    public string? BackupCorruptPath { get; }

    public bool IsReadOnlyFutureVersion { get; }

    internal SqliteConnection Connection => _connection;

    internal Lock Gate { get; } = new();

    public static string DefaultDatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VGList",
        "plana.db");

    /// <summary>打开并迁移本地数据库。若检测到文件损坏，改名备份为 plana.db.corrupt-* 并以新库启动。</summary>
    public static SqliteDatabase Open(string? filePath = null)
    {
        var path = string.IsNullOrWhiteSpace(filePath) ? DefaultDatabasePath : filePath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        try
        {
            return OpenInternal(path, recovered: false, backupPath: null);
        }
        catch (SqliteException ex) when (IsCorruptionError(ex))
        {
            SqliteConnection.ClearAllPools();
            var backup = BackupCorruptFiles(path);
            return OpenInternal(path, recovered: true, backupPath: backup);
        }
    }

    private static SqliteDatabase OpenInternal(string path, bool recovered, string? backupPath)
    {
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
        }.ToString();

        var conn = new SqliteConnection(connStr);
        try
        {
            conn.Open();
            ConfigurePragmas(conn);
            VerifyIntegrityQuick(conn);
            var isFuture = ApplyMigrations(conn);
            if (isFuture)
            {
                // 高版本库切为只读，避免旧程序破坏新格式。
                using var ro = conn.CreateCommand();
                ro.CommandText = "PRAGMA query_only = ON;";
                ro.ExecuteNonQuery();
            }

            return new SqliteDatabase(path, conn, recovered, backupPath, isFuture);
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }

    private static void ConfigurePragmas(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 3000;
            """;
        cmd.ExecuteNonQuery();
    }

    private static void VerifyIntegrityQuick(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA quick_check(1);";
        var result = cmd.ExecuteScalar()?.ToString();
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new SqliteException($"SQLite integrity check failed: {result}", 11);
        }
    }

    private static bool ApplyMigrations(SqliteConnection conn)
    {
        var current = GetCurrentVersion(conn);
        if (current > LatestSchemaVersion)
        {
            return true;
        }

        var migrations = LoadEmbeddedMigrations();
        foreach (var (version, sql) in migrations)
        {
            if (version <= current)
            {
                continue;
            }

            using var tx = conn.BeginTransaction();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }

            using (var stamp = conn.CreateCommand())
            {
                stamp.Transaction = tx;
                stamp.CommandText = "INSERT OR REPLACE INTO schema_version(version, applied_at) VALUES($v, $at);";
                stamp.Parameters.AddWithValue("$v", version);
                stamp.Parameters.AddWithValue("$at", DateTimeOffset.Now.ToString("O"));
                stamp.ExecuteNonQuery();
            }

            tx.Commit();
        }

        return false;
    }

    private static int GetCurrentVersion(SqliteConnection conn)
    {
        using var check = conn.CreateCommand();
        check.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='schema_version';";
        if (check.ExecuteScalar() is null)
        {
            return 0;
        }

        using var max = conn.CreateCommand();
        max.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version;";
        return Convert.ToInt32(max.ExecuteScalar());
    }

    private static List<(int Version, string Sql)> LoadEmbeddedMigrations()
    {
        var asm = Assembly.GetExecutingAssembly();
        var list = new List<(int, string)>();
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // 文件名格式：...Migrations.001_init.sql
            var segments = name.Split('.');
            if (segments.Length < 2)
            {
                continue;
            }

            var fileStem = segments[^2];
            var underscore = fileStem.IndexOf('_');
            var prefix = underscore > 0 ? fileStem[..underscore] : fileStem;
            if (!int.TryParse(prefix, out var version))
            {
                continue;
            }

            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            list.Add((version, reader.ReadToEnd()));
        }

        list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return list;
    }

    private static bool IsCorruptionError(SqliteException ex) =>
        ex.SqliteErrorCode is 11 or 26; // SQLITE_CORRUPT (11) / SQLITE_NOTADB (26)

    private static string BackupCorruptFiles(string path)
    {
        var stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        var backup = $"{path}.corrupt-{stamp}";
        if (File.Exists(path))
        {
            File.Move(path, backup, overwrite: true);
        }

        TryDeleteSidecar(path + "-wal");
        TryDeleteSidecar(path + "-shm");
        return backup;
    }

    private static void TryDeleteSidecar(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection.Dispose();
    }
}
