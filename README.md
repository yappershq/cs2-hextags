<div align="center">
  <h1><strong>HexTags</strong></h1>
  <p>Priority-based chat & scoreboard tags for CS2 ModSharp servers — driven by SteamID, admin flags, or VIP, with optional shared-MySQL rule sync.</p>
</div>

<p align="center">
  <a href="https://github.com/Kxnrl/modsharp-public"><img src="https://img.shields.io/badge/framework-ModSharp-5865F2?logo=github" alt="ModSharp"></a>
  <img src="https://img.shields.io/badge/game-CS2-orange" alt="CS2">
  <img src="https://img.shields.io/github/stars/yappershq/cs2-hextags?style=flat&logo=github" alt="Stars">
</p>

---

HexTags assigns each player a colored chat prefix, name/chat color, an optional name suffix, and a short scoreboard clan-tag based on a list of priority-ranked rules. Rules match on SteamID, admin permission flag, or VIP status. Rules can be loaded from the local config file or sourced from a shared MySQL database (with the config rules kept as the offline fallback and the initial DB seed), so multiple servers can stay in sync.

## 🚀 Install

Copy the build output into your ModSharp install (`<sharp>` = your `sharp` directory):

| From | To |
|------|----|
| `.build/modules/HexTags.Core/` | `<sharp>/modules/HexTags/` |
| `.build/shared/HexTags.Shared/HexTags.Shared.dll` | `<sharp>/shared/HexTags.Shared.dll` |
| `.build/configs.example/cs2-hextags.json` | `<sharp>/configs/cs2-hextags.json` |

Restart the server (or change map) to load. On first run, if `<sharp>/configs/cs2-hextags.json` is missing the plugin writes a default config there automatically — copying the example is only needed if you want the sample rule set.

Optional integrations are resolved if present: **AdminManager** (for `AdminFlag` matches), a **VIP** module (for `Vip`/`VipFlag` matches), and **ClientPreferences** (to persist the `!hidetag` toggle). The shared-MySQL sync uses the bundled `MySqlConnector`.

## ⌨️ Commands

| Command | Description |
|---------|-------------|
| `!hidetag` | Toggle your own tag on/off. The preference is saved via ClientPreferences and reapplied on rejoin. |

## ⚙️ Configuration

`configs/cs2-hextags.json` (auto-written on first run):

| Setting | Default | Meaning |
|---------|---------|---------|
| `Enabled` | `true` | Master on/off switch. |
| `UseDatabase` | `true` | When true **and** `Database.Host` is set, rules are sourced from the shared MySQL DB. The `Rules` list is still used as offline fallback and initial DB seed. |
| `ServerTag` | `""` | Identifies this server in the DB — rows with `server='all'` or `server=<ServerTag>` apply. Empty = only `all` rows. |
| `PollSeconds` | `60` | How often (seconds, min 15) to poll the DB version marker for rule changes. |
| `Database` | `{}` | MySQL connection — `Host`, `Port` (3306), `User`, `Password`, `Name`. Fill on the deployed server only; do not commit real credentials. |
| `Rules` | sample set | Ordered list of tag rules (see below). |

### Rule fields

| Field | Meaning |
|-------|---------|
| `Name` | Human label for the rule. |
| `Match` | `{ "Type": ..., "Value": ... }` — see match types below. |
| `Tag` | Chat prefix shown before the name (color tokens like `{red}` allowed). |
| `Suffix` | Optional text appended after the name. |
| `NameColor` | Color applied to the player name (`{teamcolor}` supported). |
| `ChatColor` | Color applied to the player's chat message text. |
| `ScoreboardTag` | Short clan-tag shown on the scoreboard. |
| `Priority` | Higher wins. Rules are sorted by priority descending at load; matching rules stack, with higher-priority rules filling fields first. |

### Match types

| `Type` | `Value` | Matches when |
|--------|---------|--------------|
| `SteamId` | a SteamID64 | the player's SteamID equals `Value`. |
| `AdminFlag` | an admin permission (e.g. `admin:ban`, `*`) | the player's admin has that permission. |
| `Vip` | `""` | the player is VIP. |
| `VipFlag` | a VIP flag | the player has that VIP flag. |
| `Default` | `""` | always (fallback rule). |

## 🔧 How it works

Tags are resolved per-SteamID and cached; the cache is invalidated on connect, admin-check, and disconnect. Each player's matching rules are merged by priority into a final tag, applied to chat via a `SayText2` hook and to the scoreboard via the player controller's clan-tag. When `UseDatabase` is on, a background timer polls a DB version marker every `PollSeconds` and only reloads rules when it changes; an empty DB is seeded from the config `Rules` on first connect.

## 🧩 Public API

Other plugins can hide/show a player's HexTag at runtime via the shipped `IHexTagsShared` interface (resolve in `OnAllModulesLoaded`):

```csharp
var api = sharpModuleManager
    .GetOptionalSharpModuleInterface<IHexTagsShared>(IHexTagsShared.Identity)?.Instance;

api?.SetHidden(slot, true);   // transient external hide (not saved)
bool hidden = api?.IsHidden(slot) ?? false;
```

## 📦 Build

```bash
dotnet build -c Release
```

Outputs the module to `.build/modules/HexTags.Core/HexTags.dll` (with its dependencies), the public API to `.build/shared/HexTags.Shared/HexTags.Shared.dll`, and the example config to `.build/configs.example/cs2-hextags.json`.

---

<div align="center">
  <p>Made with ❤️ by <a href="https://github.com/yappershq">yappershq</a></p>
  <p>⭐ Star this repo if you find it useful!</p>
</div>
