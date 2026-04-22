using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyReview.Data;

public class DatabaseManager
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ILogger<DatabaseManager> _logger;

    public DatabaseManager(IApplicationPaths appPaths, ILogger<DatabaseManager> logger)
    {
        _logger = logger;
        SQLitePCL.Batteries.Init();
        var dbPath = Path.Combine(appPaths.DataPath, "jellyreview.db");
        _connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared";
        InitializeSchema();
    }

    public SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public SemaphoreSlim WriteLock => _writeLock;

    public async Task ExecuteWriteAsync(Func<SqliteConnection, Task> action)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var conn = CreateConnection();
            await action(conn).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<T> ExecuteWriteAsync<T>(Func<SqliteConnection, Task<T>> action)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var conn = CreateConnection();
            return await action(conn).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void InitializeSchema()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(
                "Jellyfin.Plugin.JellyReview.Data.Schema.sql");
            if (stream == null)
            {
                _logger.LogError("JellyReview: Schema.sql embedded resource not found");
                return;
            }

            using var reader = new StreamReader(stream);
            var sql = reader.ReadToEnd();

            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            cmd.ExecuteNonQuery();

            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();

            ApplyMigrations(conn);

            _logger.LogInformation("JellyReview: database initialized at {Path}", GetDbPath());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JellyReview: failed to initialize database schema");
        }
    }

    private string GetDbPath()
    {
        var builder = new SqliteConnectionStringBuilder(_connectionString);
        return builder.DataSource;
    }

    private void ApplyMigrations(SqliteConnection conn)
    {
        EnsureColumns(conn, "media_records", new Dictionary<string, string>
        {
            ["status"] = "TEXT NOT NULL DEFAULT 'active'",
            ["metadata_hash"] = "TEXT",
            ["first_seen_at"] = "TEXT",
            ["last_seen_at"] = "TEXT",
            ["last_synced_at"] = "TEXT",
        });

        EnsureColumns(conn, "review_decisions", new Dictionary<string, string>
        {
            ["source"] = "TEXT NOT NULL DEFAULT 'sync_reconciliation'",
            ["notes"] = "TEXT",
            ["needs_resync"] = "INTEGER NOT NULL DEFAULT 0",
            ["created_at"] = "TEXT",
            ["updated_at"] = "TEXT",
        });

        EnsureColumns(conn, "viewer_profiles", new Dictionary<string, string>
        {
            ["active_rule_set_id"] = "TEXT",
            ["is_active"] = "INTEGER NOT NULL DEFAULT 1",
            ["updated_at"] = "TEXT",
        });

        EnsureColumns(conn, "viewer_decisions", new Dictionary<string, string>
        {
            ["source"] = "TEXT NOT NULL DEFAULT 'manual_review'",
            ["notes"] = "TEXT",
            ["needs_resync"] = "INTEGER NOT NULL DEFAULT 0",
            ["created_at"] = "TEXT",
            ["updated_at"] = "TEXT",
        });

        EnsureColumns(conn, "review_rules", new Dictionary<string, string>
        {
            ["viewer_profile_id"] = "TEXT",
            ["updated_at"] = "TEXT",
        });

        EnsureColumns(conn, "notification_channels", new Dictionary<string, string>
        {
            ["notify_on_digest"] = "INTEGER NOT NULL DEFAULT 0",
            ["updated_at"] = "TEXT",
        });

        EnsureColumns(conn, "notification_deliveries", new Dictionary<string, string>
        {
            ["provider_message_id"] = "TEXT",
            ["action_token_hash"] = "TEXT",
            ["actioned_at"] = "TEXT",
            ["error_detail"] = "TEXT",
        });

        BackfillColumn(conn, "media_records", "status", "'active'");
        BackfillColumn(conn, "media_records", "first_seen_at", "datetime('now')");
        BackfillColumn(conn, "media_records", "last_seen_at", "datetime('now')");

        BackfillColumn(conn, "review_decisions", "source", "'sync_reconciliation'");
        BackfillColumn(conn, "review_decisions", "needs_resync", "0");
        BackfillColumn(conn, "review_decisions", "created_at", "datetime('now')");
        BackfillColumn(conn, "review_decisions", "updated_at", "datetime('now')");

        BackfillColumn(conn, "viewer_profiles", "is_active", "1");
        BackfillColumn(conn, "viewer_profiles", "updated_at", "datetime('now')");

        BackfillColumn(conn, "viewer_decisions", "source", "'manual_review'");
        BackfillColumn(conn, "viewer_decisions", "needs_resync", "0");
        BackfillColumn(conn, "viewer_decisions", "created_at", "datetime('now')");
        BackfillColumn(conn, "viewer_decisions", "updated_at", "datetime('now')");

        BackfillColumn(conn, "review_rules", "updated_at", "datetime('now')");

        BackfillColumn(conn, "notification_channels", "notify_on_digest", "0");
        BackfillColumn(conn, "notification_channels", "updated_at", "datetime('now')");
    }

    private void EnsureColumns(SqliteConnection conn, string table, IReadOnlyDictionary<string, string> requiredColumns)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA table_info({table})";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                existing.Add(reader.GetString(1));
            }
        }

        foreach (var column in requiredColumns)
        {
            if (existing.Contains(column.Key))
            {
                continue;
            }

            using var alter = conn.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column.Key} {column.Value}";
            alter.ExecuteNonQuery();
            _logger.LogInformation("JellyReview: added missing column {Column} to {Table}", column.Key, table);
        }
    }

    private static void BackfillColumn(SqliteConnection conn, string table, string column, string sqlValue)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE {table} SET {column} = {sqlValue} WHERE {column} IS NULL";
        cmd.ExecuteNonQuery();
    }
}
