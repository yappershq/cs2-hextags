using HexTags.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sharp.Shared.Enums;
using Sharp.Shared.Listeners;
using Sharp.Shared.Managers;
using Sharp.Shared.Objects;
using Sharp.Shared.Types;
using Sharp.Shared.Units;

namespace HexTags.Core.Modules;

internal sealed class HideStateModule : IModule, IClientListener, IHexTagsShared
{
    private const string CookieKey = "hextags.hidetag";

    private readonly InterfaceBridge           _bridge;
    private readonly HiddenTagState            _state;
    private readonly TagResolverModule         _resolver;
    private readonly ScoreboardTagModule       _scoreboard;
    private readonly ILogger<HideStateModule>  _logger;

    private readonly IClientManager.DelegateClientCommand _onHideTag;
    private          System.IDisposable?                  _loadSub;

    int IClientListener.ListenerVersion  => IClientListener.ApiVersion;
    int IClientListener.ListenerPriority => 0;

    public HideStateModule(
        InterfaceBridge          bridge,
        HiddenTagState           state,
        TagResolverModule        resolver,
        ScoreboardTagModule      scoreboard,
        ILogger<HideStateModule> logger)
    {
        _bridge     = bridge;
        _state      = state;
        _resolver   = resolver;
        _scoreboard = scoreboard;
        _logger     = logger;

        _onHideTag = OnHideTagCommand;
    }

    public bool Init()
    {
        _bridge.ClientManager.InstallClientListener(this);
        _bridge.ClientManager.InstallCommandCallback("hidetag", _onHideTag);
        return true;
    }

    public void OnPostInit(ServiceProvider provider)
    {
        _bridge.SharpModuleManager.RegisterSharpModuleInterface<IHexTagsShared>(
            _bridge.Module, IHexTagsShared.Identity, this);
        _logger.LogInformation("[HexTags] Registered IHexTagsShared ({Id})", IHexTagsShared.Identity);
    }

    public void OnAllModulesLoaded(ServiceProvider provider)
    {
        // Cookies often load AFTER OnClientPostAdminCheck, so apply the saved preference whenever the
        // client's cookies finish loading (the reliable signal).
        _loadSub = _bridge.ClientPreferences?.ListenOnLoad(ApplyPrefFromCookie);
    }

    public void Shutdown()
    {
        _loadSub?.Dispose();
        _bridge.ClientManager.RemoveClientListener(this);
        _bridge.ClientManager.RemoveCommandCallback("hidetag", _onHideTag);
    }

    private void ApplyPrefFromCookie(IGameClient client)
    {
        var cp = _bridge.ClientPreferences;
        if (cp is null || !cp.IsLoaded(client.SteamId))
            return;

        var hidden = cp.GetCookie(client.SteamId, CookieKey)?.GetNumber() != 0;
        _state.SetPref(client.SteamId, hidden);
        Refresh(client);
    }

    // ── IHexTagsShared ─────────────────────────────────────────────────────────

    void IHexTagsShared.SetHidden(int slot, bool hidden)
    {
        var client = _bridge.ClientManager.GetGameClient(new PlayerSlot((byte)slot));
        if (client is null)
            return;

        _state.SetExternal(client.SteamId, hidden);
        Refresh(client);
    }

    bool IHexTagsShared.IsHidden(int slot)
    {
        var client = _bridge.ClientManager.GetGameClient(new PlayerSlot((byte)slot));
        return client is not null && _state.IsHidden(client.SteamId);
    }

    // ── IClientListener ────────────────────────────────────────────────────────

    void IClientListener.OnClientPostAdminCheck(IGameClient client) => ApplyPrefFromCookie(client);

    void IClientListener.OnClientDisconnected(IGameClient client, NetworkDisconnectionReason reason)
        => _state.Clear(client.SteamId);

    void IClientListener.OnClientConnected(IGameClient c)                                   { }
    void IClientListener.OnClientPutInServer(IGameClient c)                                 { }
    void IClientListener.OnClientDisconnecting(IGameClient c, NetworkDisconnectionReason r) { }
    void IClientListener.OnClientSettingChanged(IGameClient c)                              { }
    void IClientListener.OnAdminCacheReload()                                               { }
    bool IClientListener.OnClientPreAdminCheck(IGameClient c)                               => false;

    ECommandAction IClientListener.OnClientSayCommand(
        IGameClient c, bool teamOnly, bool isCommand, string commandName, string message)
        => ECommandAction.Skipped;

    // ── Command ────────────────────────────────────────────────────────────────

    private ECommandAction OnHideTagCommand(IGameClient client, StringCommand command)
    {
        if (client.IsFakeClient)
            return ECommandAction.Skipped;

        var cp = _bridge.ClientPreferences;
        if (cp is null || !cp.IsLoaded(client.SteamId))
        {
            _logger.LogWarning("[HexTags] !hidetag: ClientPreferences not available for SteamId={SteamId}", client.SteamId);
            return ECommandAction.Handled;
        }

        var current = cp.GetCookie(client.SteamId, CookieKey)?.GetNumber() != 0;
        var newValue = !current;

        cp.SetCookie(client.SteamId, CookieKey, newValue);
        _state.SetPref(client.SteamId, newValue);
        Refresh(client);

        var msg = newValue
            ? " \x01[\x06HexTags\x01] Your tag is now \x07hidden\x01."
            : " \x01[\x06HexTags\x01] Your tag is now \x06visible\x01.";

        client.Print(HudPrintChannel.Chat, msg);

        return ECommandAction.Handled;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void Refresh(IGameClient client)
    {
        _resolver.Invalidate(client.SteamId);
        _scoreboard.RefreshFor(client);
    }
}
