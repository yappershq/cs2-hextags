namespace HexTags.Shared;

public interface IHexTagsShared
{
    public const string Identity = nameof(IHexTagsShared);

    /// <summary>External "hide mode": hide/show this player's HexTag everywhere (transient, not saved).</summary>
    void SetHidden(int slot, bool hidden);

    /// <summary>Effective hidden state — true if external hide-mode OR the player's saved preference is on.</summary>
    bool IsHidden(int slot);
}
