using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HexTags.Core.Configuration;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace HexTags.Core.Database;

/// <summary>
///     Thin MySqlConnector-backed rule source for HexTags. Holds only a connection
///     string and opens a pooled connection per operation. Every public method is
///     defensive: failures are logged and degrade gracefully (never thrown into the
///     game thread). JSON config remains the authoritative offline fallback.
/// </summary>
internal sealed class HexTagsDb : IDisposable
{
    private readonly ILogger _logger;
    private string?          _connectionString;

    public HexTagsDb(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    ///     Build the connection string, open a probe connection and run SELECT 1.
    ///     The shared MySQL box is connection-constrained, so the pool is capped low.
    /// </summary>
    public bool Connect(DatabaseConfig cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg.Host) || string.IsNullOrWhiteSpace(cfg.Name))
        {
            _logger.LogWarning("[HexTags] DB host/name not configured");
            return false;
        }

        var cs =
            $"Server={cfg.Host};Port={cfg.Port};Database={cfg.Name};" +
            $"User ID={cfg.User};Password={cfg.Password};" +
            "AllowPublicKeyRetrieval=true;SslMode=Preferred;" +
            "Maximum Pool Size=2;Minimum Pool Size=0;";

        try
        {
            using var conn = new MySqlConnection(cs);
            conn.Open();
            using var cmd = new MySqlCommand("SELECT 1", conn);
            cmd.ExecuteScalar();

            _connectionString = cs;
            _logger.LogInformation("[HexTags] Connected to rule DB {Host}:{Port}/{Db}", cfg.Host, cfg.Port, cfg.Name);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "[HexTags] DB connect failed ({Host}:{Port}/{Db}); falling back to JSON",
                cfg.Host, cfg.Port, cfg.Name);
            return false;
        }
    }

    public async Task EnsureSchemaAsync()
    {
        const string createRules =
            """
            CREATE TABLE IF NOT EXISTS hextags_rules (
              id INT AUTO_INCREMENT PRIMARY KEY,
              enabled TINYINT(1) NOT NULL DEFAULT 1,
              name VARCHAR(64) NOT NULL DEFAULT '',
              match_type VARCHAR(16) NOT NULL DEFAULT 'Default',
              match_value VARCHAR(64) NOT NULL DEFAULT '',
              tag VARCHAR(64) NOT NULL DEFAULT '',
              suffix VARCHAR(64) NOT NULL DEFAULT '',
              name_color VARCHAR(32) NOT NULL DEFAULT '',
              chat_color VARCHAR(32) NOT NULL DEFAULT '',
              scoreboard_tag VARCHAR(32) NOT NULL DEFAULT '',
              priority INT NOT NULL DEFAULT 0,
              server VARCHAR(64) NOT NULL DEFAULT 'all',
              updated_at DATETIME NOT NULL
            );
            """;

        const string createMeta =
            """
            CREATE TABLE IF NOT EXISTS hextags_meta (
              name VARCHAR(32) PRIMARY KEY,
              value BIGINT NOT NULL
            );
            """;

        const string seedMeta =
            "INSERT IGNORE INTO hextags_meta(name, value) VALUES('rules_version', 0);";

        try
        {
            await using var conn = await OpenAsync();
            if (conn is null)
                return;

            await using (var cmd = new MySqlCommand(createRules, conn))
                await cmd.ExecuteNonQueryAsync();
            await using (var cmd = new MySqlCommand(createMeta, conn))
                await cmd.ExecuteNonQueryAsync();
            await using (var cmd = new MySqlCommand(seedMeta, conn))
                await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "[HexTags] EnsureSchema failed");
        }
    }

    public async Task<int> CountRulesAsync()
    {
        try
        {
            await using var conn = await OpenAsync();
            if (conn is null)
                return 0;

            await using var cmd = new MySqlCommand("SELECT COUNT(*) FROM hextags_rules", conn);
            var result = await cmd.ExecuteScalarAsync();
            return result is null or DBNull ? 0 : Convert.ToInt32(result);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "[HexTags] CountRules failed");
            return 0;
        }
    }

    /// <summary>
    ///     Seed the (empty) DB from the JSON rules, then bump the version marker so
    ///     other servers reload. Runs inside a transaction.
    /// </summary>
    public async Task SeedFromRulesAsync(IEnumerable<TagRule> rules)
    {
        const string insert =
            """
            INSERT INTO hextags_rules
              (enabled, name, match_type, match_value, tag, suffix, name_color, chat_color, scoreboard_tag, priority, server, updated_at)
            VALUES
              (1, @name, @mtype, @mvalue, @tag, @suffix, @ncolor, @ccolor, @stag, @priority, 'all', @updated);
            """;

        const string bump =
            "UPDATE hextags_meta SET value = value + 1 WHERE name = 'rules_version';";

        try
        {
            await using var conn = await OpenAsync();
            if (conn is null)
                return;

            await using var tx = await conn.BeginTransactionAsync();

            var now = DateTime.UtcNow;
            foreach (var rule in rules)
            {
                await using var cmd = new MySqlCommand(insert, conn, tx);
                cmd.Parameters.AddWithValue("@name",     rule.Name);
                cmd.Parameters.AddWithValue("@mtype",    rule.Match.Type);
                cmd.Parameters.AddWithValue("@mvalue",   rule.Match.Value);
                cmd.Parameters.AddWithValue("@tag",      rule.Tag);
                cmd.Parameters.AddWithValue("@suffix",   rule.Suffix);
                cmd.Parameters.AddWithValue("@ncolor",   rule.NameColor);
                cmd.Parameters.AddWithValue("@ccolor",   rule.ChatColor);
                cmd.Parameters.AddWithValue("@stag",     rule.ScoreboardTag);
                cmd.Parameters.AddWithValue("@priority", rule.Priority);
                cmd.Parameters.AddWithValue("@updated",  now);
                await cmd.ExecuteNonQueryAsync();
            }

            await using (var cmd = new MySqlCommand(bump, conn, tx))
                await cmd.ExecuteNonQueryAsync();

            await tx.CommitAsync();
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "[HexTags] SeedFromRules failed");
        }
    }

    public async Task<List<TagRule>> LoadRulesAsync(string serverTag)
    {
        const string select =
            """
            SELECT name, match_type, match_value, tag, suffix, name_color, chat_color, scoreboard_tag, priority
            FROM hextags_rules
            WHERE enabled = 1 AND (server = 'all' OR server = @tag)
            ORDER BY priority DESC;
            """;

        var rules = new List<TagRule>();
        try
        {
            await using var conn = await OpenAsync();
            if (conn is null)
                return rules;

            await using var cmd = new MySqlCommand(select, conn);
            cmd.Parameters.AddWithValue("@tag", serverTag ?? string.Empty);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rules.Add(new TagRule
                {
                    Name          = reader.GetString(0),
                    Match         = new MatchSpec { Type = reader.GetString(1), Value = reader.GetString(2) },
                    Tag           = reader.GetString(3),
                    Suffix        = reader.GetString(4),
                    NameColor     = reader.GetString(5),
                    ChatColor     = reader.GetString(6),
                    ScoreboardTag = reader.GetString(7),
                    Priority      = reader.GetInt32(8),
                });
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "[HexTags] LoadRules failed");
        }

        return rules;
    }

    public async Task<long> GetVersionAsync()
    {
        try
        {
            await using var conn = await OpenAsync();
            if (conn is null)
                return 0;

            await using var cmd = new MySqlCommand(
                "SELECT value FROM hextags_meta WHERE name = 'rules_version'", conn);
            var result = await cmd.ExecuteScalarAsync();
            return result is null or DBNull ? 0 : Convert.ToInt64(result);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "[HexTags] GetVersion failed");
            return 0;
        }
    }

    private async Task<MySqlConnection?> OpenAsync()
    {
        if (_connectionString is null)
            return null;

        var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        return conn;
    }

    public void Dispose()
    {
        // Connections are opened per-op and disposed there; clear the cached string
        // so a stale handle can't be reused after shutdown.
        _connectionString = null;
    }
}
