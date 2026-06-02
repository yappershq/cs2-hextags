using System.Text.Json.Serialization;

namespace HexTags.Core.Configuration;

internal sealed class TagRule
{
    [JsonPropertyName("Name")]     public string   Name          { get; set; } = string.Empty;
    [JsonPropertyName("Match")]    public MatchSpec Match         { get; set; } = new();
    [JsonPropertyName("Tag")]      public string   Tag           { get; set; } = string.Empty;
    [JsonPropertyName("Suffix")]   public string   Suffix        { get; set; } = string.Empty;
    [JsonPropertyName("NameColor")] public string  NameColor     { get; set; } = string.Empty;
    [JsonPropertyName("ChatColor")] public string  ChatColor     { get; set; } = string.Empty;
    [JsonPropertyName("ScoreboardTag")] public string ScoreboardTag { get; set; } = string.Empty;
    [JsonPropertyName("Priority")] public int      Priority      { get; set; }
}

internal sealed class MatchSpec
{
    [JsonPropertyName("Type")]  public string Type  { get; set; } = "Default";
    [JsonPropertyName("Value")] public string Value { get; set; } = string.Empty;
}
