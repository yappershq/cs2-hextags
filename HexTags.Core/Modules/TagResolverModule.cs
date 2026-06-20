using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using HexTags.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sharp.Modules.AdminManager.Shared;
using Sharp.Shared.Enums;
using Sharp.Shared.Listeners;
using Sharp.Shared.Objects;
using Vip.Shared;

namespace HexTags.Core.Modules;

internal sealed class ResolvedTag
{
    internal string Tag           { get; set; } = string.Empty;
    internal string Suffix        { get; set; } = string.Empty;
    internal string NameColor     { get; set; } = string.Empty;
    internal string ChatColor     { get; set; } = string.Empty;
    internal string ScoreboardTag { get; set; } = string.Empty;
}

internal sealed class TagResolverModule : IModule, IClientListener
{
    private readonly InterfaceBridge                              _bridge;
    private readonly ILogger<TagResolverModule>                   _logger;
    private readonly HexTagsConfig                               _config;
    private readonly HiddenTagState                              _hidden;
    private readonly ConcurrentDictionary<ulong, ResolvedTag>    _cache = new();

    // Swappable rule set. Seeded from JSON at construction so JSON-only servers
    // work without any DB; RuleSyncModule may replace it live via ReplaceRules.
    // volatile so the off-thread DB poller's swap is visible to game-thread reads.
    private volatile TagRule[] _rules;

    private IVipShared? _vip;

    int IClientListener.ListenerVersion  => IClientListener.ApiVersion;
    int IClientListener.ListenerPriority => 0;

    public TagResolverModule(InterfaceBridge bridge, ILogger<TagResolverModule> logger, HexTagsConfig config, HiddenTagState hidden)
    {
        _bridge = bridge;
        _logger = logger;
        _config = config;
        _hidden = hidden;

        // Config.Rules is already sorted DESC at load; re-sort defensively.
        _rules = config.Rules.OrderByDescending(static r => r.Priority).ToArray();
    }

    /// <summary>
    ///     Atomically replace the active rule set (e.g. from the DB sync poller).
    ///     Sorts DESC by priority, swaps the volatile reference, then clears the
    ///     resolution cache so consumers re-resolve against the new rules live.
    /// </summary>
    internal void ReplaceRules(IEnumerable<TagRule> rules)
    {
        var arr = rules.OrderByDescending(static r => r.Priority).ToArray();
        _rules = arr;        // volatile swap — readers see the whole new array atomically
        _cache.Clear();
        _logger.LogInformation("[HexTags] Rule set replaced ({Count} rules)", arr.Length);
    }

    public bool Init()
    {
        _bridge.ClientManager.InstallClientListener(this);
        return true;
    }

    public void OnAllModulesLoaded(ServiceProvider provider)
    {
        _vip = _bridge.SharpModuleManager
            .GetOptionalSharpModuleInterface<IVipShared>(IVipShared.Identity)?.Instance;

        if (_vip is null)
            _logger.LogInformation("[HexTags] VIP module not present — Vip/VipFlag rules will never match");

        _cache.Clear();
    }

    public void Shutdown()
    {
        _bridge.ClientManager.RemoveClientListener(this);
        _cache.Clear();
    }

    internal ResolvedTag Resolve(ulong steamId)
        => _cache.GetOrAdd(steamId, sid => ComputeTag(sid));

    internal void Invalidate(ulong steamId) => _cache.TryRemove(steamId, out _);

    private ResolvedTag ComputeTag(ulong steamId)
    {
        if (!_config.Enabled)
            return new ResolvedTag();

        if (_hidden.IsHidden(steamId))
            return new ResolvedTag();

        var am = _bridge.AdminManager;
        var resolved = new ResolvedTag();

        // VIP is positioned contextually: if the player has a higher role
        // (admin/dev) it becomes a trailing suffix; if VIP is the top role it
        // becomes the prefix. The VIP rule supplies BOTH forms — its Tag is the
        // prefix form, its Suffix is the trailing form — so admins control each
        // independently in config.
        var vipMatched = false;
        var vipPrefix  = string.Empty;
        var vipSuffix  = string.Empty;

        // Snapshot the volatile rule array once so a concurrent ReplaceRules swap
        // can't change what we iterate mid-loop.
        var rules = _rules;

        // Rules pre-sorted by priority DESC at load time. The dominant
        // (highest-priority) non-VIP rule sets the prefix Tag; each remaining
        // field fills from the first matching rule that sets it.
        foreach (var rule in rules)
        {
            if (!Matches(rule, steamId, am))
                continue;

            var isVipRule = rule.Match.Type is "Vip" or "VipFlag";

            if (isVipRule)
            {
                if (!vipMatched)
                {
                    vipMatched = true;
                    vipPrefix  = rule.Tag;
                    vipSuffix  = rule.Suffix;
                }
            }
            else
            {
                if (resolved.Tag.Length    == 0 && rule.Tag.Length    > 0) resolved.Tag    = rule.Tag;
                if (resolved.Suffix.Length == 0 && rule.Suffix.Length > 0) resolved.Suffix = rule.Suffix;
            }

            if (resolved.NameColor.Length     == 0 && rule.NameColor.Length     > 0) resolved.NameColor     = rule.NameColor;
            if (resolved.ChatColor.Length     == 0 && rule.ChatColor.Length     > 0) resolved.ChatColor     = rule.ChatColor;
            if (resolved.ScoreboardTag.Length == 0 && rule.ScoreboardTag.Length > 0) resolved.ScoreboardTag = rule.ScoreboardTag;
        }

        if (vipMatched)
        {
            if (resolved.Tag.Length > 0)
            {
                // Higher role owns the prefix -> VIP trails the name as a suffix.
                if (resolved.Suffix.Length == 0)
                    resolved.Suffix = vipSuffix.Length > 0 ? vipSuffix : vipPrefix;
            }
            else
            {
                // VIP is the top role -> VIP owns the prefix, no suffix.
                resolved.Tag = vipPrefix.Length > 0 ? vipPrefix : vipSuffix;
            }
        }

        return resolved;
    }

    private bool Matches(TagRule rule, ulong steamId, IAdminManager? am)
    {
        return rule.Match.Type switch
        {
            "AdminFlag" => am?.GetAdmin(steamId)?.HasPermission(rule.Match.Value) ?? false,
            "Vip"       => _vip?.IsVip(steamId) ?? false,
            "VipFlag"   => _vip?.HasFlag(steamId, rule.Match.Value) ?? false,
            "SteamId"   => string.Equals(steamId.ToString(), rule.Match.Value,
                               System.StringComparison.Ordinal),
            "Default"   => true,
            _           => false,
        };
    }

    // IClientListener — invalidate cache on connect/disconnect so tags are re-resolved.
    void IClientListener.OnClientConnected(IGameClient c)                                        => Invalidate(c.SteamId);
    void IClientListener.OnClientPostAdminCheck(IGameClient c)                                   => Invalidate(c.SteamId);
    void IClientListener.OnClientDisconnected(IGameClient c, NetworkDisconnectionReason r)       => Invalidate(c.SteamId);
    void IClientListener.OnAdminCacheReload()                                                    => _cache.Clear();

    void IClientListener.OnClientPutInServer(IGameClient c)                                      { }
    void IClientListener.OnClientDisconnecting(IGameClient c, NetworkDisconnectionReason r)      { }
    void IClientListener.OnClientSettingChanged(IGameClient c)                                   { }
    bool IClientListener.OnClientPreAdminCheck(IGameClient c)                                    => false;

    ECommandAction IClientListener.OnClientSayCommand(
        IGameClient c, bool teamOnly, bool isCommand, string commandName, string message)
        => ECommandAction.Skipped;
}
