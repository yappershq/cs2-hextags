using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace HexTags.Core.Configuration;

internal sealed class DatabaseConfig
{
    [JsonPropertyName("Host")]     public string Host     { get; set; } = string.Empty;
    [JsonPropertyName("Port")]     public int    Port     { get; set; } = 3306;
    [JsonPropertyName("User")]     public string User     { get; set; } = string.Empty;
    [JsonPropertyName("Password")] public string Password { get; set; } = string.Empty;
    [JsonPropertyName("Name")]     public string Name     { get; set; } = string.Empty;
}

internal sealed class HexTagsConfig
{
    [JsonPropertyName("Enabled")]     public bool           Enabled     { get; set; } = true;

    // When true (and a DB host is configured) rules are sourced from the shared
    // MySQL DB; the JSON Rules below are kept as offline fallback + initial seed.
    [JsonPropertyName("UseDatabase")] public bool           UseDatabase { get; set; } = true;

    // Identifies this server in the DB (rows with server='all' or server=ServerTag apply).
    [JsonPropertyName("ServerTag")]   public string         ServerTag   { get; set; } = string.Empty;

    // How often (seconds, min 15) to poll the DB version marker for changes.
    [JsonPropertyName("PollSeconds")] public int            PollSeconds { get; set; } = 60;

    [JsonPropertyName("Database")]    public DatabaseConfig Database    { get; set; } = new();

    [JsonPropertyName("Rules")]       public List<TagRule>  Rules       { get; set; } = [];

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true,
        WriteIndented               = true,
    };

    public static HexTagsConfig Load(string sharpPath, ILogger logger)
    {
        var path = Path.Combine(sharpPath, "configs", "cs2-hextags.json");
        try
        {
            if (!File.Exists(path))
            {
                var def = BuildDefault();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(def, JsonOpts));
                logger.LogInformation("[HexTags] Wrote default config to {Path}", path);
                return def;
            }

            var cfg = JsonSerializer.Deserialize<HexTagsConfig>(File.ReadAllText(path), JsonOpts);
            if (cfg is null)
            {
                logger.LogError("[HexTags] cs2-hextags.json deserialized to null, using defaults");
                return BuildDefault();
            }

            // Sort rules by priority descending once at load time.
            cfg.Rules.Sort(static (a, b) => b.Priority.CompareTo(a.Priority));
            logger.LogInformation("[HexTags] Loaded config from {Path} ({Count} rules)", path, cfg.Rules.Count);
            return cfg;
        }
        catch (Exception e)
        {
            logger.LogError(e, "[HexTags] Failed to load cs2-hextags.json, using defaults");
            return BuildDefault();
        }
    }

    private static HexTagsConfig BuildDefault() => new()
    {
        Enabled     = true,
        UseDatabase = true,
        ServerTag   = string.Empty,
        PollSeconds = 60,
        Database    = new DatabaseConfig(),
        Rules =
        [
            new()
            {
                Name          = "Default",
                Match         = new MatchSpec { Type = "Default", Value = "" },
                Tag           = "",
                NameColor     = "",
                ChatColor     = "",
                ScoreboardTag = "",
                Priority      = 0,
            },
        ],
    };
}
