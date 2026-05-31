using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace HexTags.Core.Configuration;

internal sealed class HexTagsConfig
{
    [JsonPropertyName("Enabled")] public bool          Enabled { get; set; } = true;
    [JsonPropertyName("Rules")]   public List<TagRule> Rules   { get; set; } = [];

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
        Enabled = true,
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
