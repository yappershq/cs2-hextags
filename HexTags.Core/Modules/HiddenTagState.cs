using System.Collections.Concurrent;

namespace HexTags.Core.Modules;

/// <summary>
///     Thread-safe store for hide-state. Two independent sets:
///     - external: transient API-driven hide (e.g. another plugin hides tags during a round)
///     - pref:     persistent player preference loaded from clientprefs
///
///     A player is considered hidden if they appear in EITHER set.
/// </summary>
internal sealed class HiddenTagState
{
    private readonly ConcurrentDictionary<ulong, byte> _external = new();
    private readonly ConcurrentDictionary<ulong, byte> _pref     = new();

    internal bool IsHidden(ulong steamId)
        => _external.ContainsKey(steamId) || _pref.ContainsKey(steamId);

    internal void SetExternal(ulong steamId, bool hidden)
    {
        if (hidden)
            _external[steamId] = 0;
        else
            _external.TryRemove(steamId, out _);
    }

    internal void SetPref(ulong steamId, bool hidden)
    {
        if (hidden)
            _pref[steamId] = 0;
        else
            _pref.TryRemove(steamId, out _);
    }

    /// <summary>Remove from both sets. Call on disconnect.</summary>
    internal void Clear(ulong steamId)
    {
        _external.TryRemove(steamId, out _);
        _pref.TryRemove(steamId, out _);
    }
}
