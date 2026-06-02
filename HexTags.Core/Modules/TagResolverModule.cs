using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<ulong, ResolvedTag>    _cache = new();

    private IVipShared? _vip;

    int IClientListener.ListenerVersion  => IClientListener.ApiVersion;
    int IClientListener.ListenerPriority => 0;

    public TagResolverModule(InterfaceBridge bridge, ILogger<TagResolverModule> logger, HexTagsConfig config)
    {
        _bridge = bridge;
        _logger = logger;
        _config = config;
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

        var am = _bridge.AdminManager;
        var resolved = new ResolvedTag();

        // Rules pre-sorted by priority DESC at load time. Walk every match
        // and let each field fill from the first matching rule that sets it
        // — so an Admin rule (high pri, sets Tag/NameColor) can co-exist
        // with a VIP rule (lower pri, sets Suffix). Admin-only player gets
        // just the admin prefix; VIP-only gets the VIP prefix + suffix it
        // configured; Admin+VIP gets admin prefix + VIP suffix.
        foreach (var rule in _config.Rules)
        {
            if (!Matches(rule, steamId, am))
                continue;

            if (resolved.Tag.Length           == 0 && rule.Tag.Length           > 0) resolved.Tag           = rule.Tag;
            if (resolved.Suffix.Length        == 0 && rule.Suffix.Length        > 0) resolved.Suffix        = rule.Suffix;
            if (resolved.NameColor.Length     == 0 && rule.NameColor.Length     > 0) resolved.NameColor     = rule.NameColor;
            if (resolved.ChatColor.Length     == 0 && rule.ChatColor.Length     > 0) resolved.ChatColor     = rule.ChatColor;
            if (resolved.ScoreboardTag.Length == 0 && rule.ScoreboardTag.Length > 0) resolved.ScoreboardTag = rule.ScoreboardTag;

            // Short-circuit when every slot is filled.
            if (resolved.Tag.Length > 0 && resolved.Suffix.Length > 0
                && resolved.NameColor.Length > 0 && resolved.ChatColor.Length > 0
                && resolved.ScoreboardTag.Length > 0)
                break;
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
