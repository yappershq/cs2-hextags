using Sharp.Shared.GameEntities;

namespace HexTags.Core.Utils;

internal static class ClanTagHelper
{
    internal static void Update(IPlayerController controller, string clanTag)
    {
        if (controller is null || !controller.IsValid()) return;
        var name = controller.PlayerName ?? string.Empty;
        if (!string.IsNullOrEmpty(name) && char.IsWhiteSpace(name[^1]))
            controller.PlayerName = name.TrimEnd();
        else
            controller.PlayerName = name + " ";
        controller.SetClanTag(clanTag ?? string.Empty);
    }
}
