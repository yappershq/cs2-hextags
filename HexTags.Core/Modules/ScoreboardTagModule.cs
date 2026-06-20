using System;
using HexTags.Core.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sharp.Extensions.GameEventManager;
using Sharp.Shared.Enums;
using Sharp.Shared.Listeners;
using Sharp.Shared.Objects;

namespace HexTags.Core.Modules;

internal sealed class ScoreboardTagModule : IModule, IClientListener
{
    private readonly InterfaceBridge        _bridge;
    private readonly TagResolverModule      _resolver;
    private readonly ILogger<ScoreboardTagModule> _logger;
    private          IGameEventManager?     _eventManager;

    int IClientListener.ListenerVersion  => IClientListener.ApiVersion;
    int IClientListener.ListenerPriority => 0;

    public ScoreboardTagModule(InterfaceBridge bridge, TagResolverModule resolver, ILogger<ScoreboardTagModule> logger)
    {
        _bridge   = bridge;
        _resolver = resolver;
        _logger   = logger;
    }

    public bool Init()
    {
        _bridge.ClientManager.InstallClientListener(this);
        return true;
    }

    public void OnAllModulesLoaded(ServiceProvider provider)
    {
        _eventManager = provider.GetRequiredService<IGameEventManager>();
        _eventManager.ListenEvent("round_start", OnRoundStart);
    }

    public void Shutdown()
    {
        _bridge.ClientManager.RemoveClientListener(this);
    }

    private void OnRoundStart(IGameEvent _) => ApplyForAll();

    private void ApplyForAll()
    {
        foreach (var client in _bridge.ClientManager.GetGameClients(inGame: true))
            ApplyFor(client);
    }

    internal void RefreshFor(IGameClient client) => ApplyFor(client);

    private void ApplyFor(IGameClient client)
    {
        if (client.IsFakeClient) return;

        if (client.GetPlayerController() is not { } ctrl) return;

        var tag = _resolver.Resolve(client.SteamId);

        try
        {
            ClanTagHelper.Update(ctrl, tag.ScoreboardTag);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "[HexTags] ScoreboardTagModule.ApplyFor threw for SteamId={SteamId}", client.SteamId);
        }
    }

    void IClientListener.OnClientPostAdminCheck(IGameClient c)                                   => ApplyFor(c);
    void IClientListener.OnClientConnected(IGameClient c)                                        { }
    void IClientListener.OnClientPutInServer(IGameClient c)                                      { }
    void IClientListener.OnClientDisconnected(IGameClient c, NetworkDisconnectionReason r)       { }
    void IClientListener.OnClientDisconnecting(IGameClient c, NetworkDisconnectionReason r)      { }
    void IClientListener.OnClientSettingChanged(IGameClient c)                                   { }
    void IClientListener.OnAdminCacheReload()                                                    { }
    bool IClientListener.OnClientPreAdminCheck(IGameClient c)                                    => false;

    ECommandAction IClientListener.OnClientSayCommand(
        IGameClient c, bool teamOnly, bool isCommand, string commandName, string message)
        => ECommandAction.Skipped;
}
