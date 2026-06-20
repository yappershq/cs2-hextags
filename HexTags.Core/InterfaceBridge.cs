using System;
using Microsoft.Extensions.Logging;
using Sharp.Modules.AdminManager.Shared;
using Sharp.Modules.ClientPreferences.Shared;
using Sharp.Shared;
using Sharp.Shared.Managers;

namespace HexTags.Core;

internal sealed class InterfaceBridge
{
    internal string  DllPath   { get; }
    internal string  SharpPath { get; }
    internal Version Version   { get; }
    internal bool    HotReload { get; }
    internal bool    Debug     { get; }

    internal IModSharpModule     Module             { get; }
    internal ISharedSystem       SharedSystem       { get; }
    internal ISharpModuleManager SharpModuleManager { get; }

    internal IClientManager  ClientManager  { get; }
    internal IModSharp       ModSharp       { get; }
    internal ILoggerFactory  LoggerFactory  { get; }

    internal IAdminManager?     AdminManager      { get; private set; }
    internal IClientPreference? ClientPreferences { get; private set; }

    public InterfaceBridge(
        string          dllPath,
        string          sharpPath,
        Version         version,
        ISharedSystem   sharedSystem,
        IModSharpModule module,
        bool            hotReload,
        bool            debug)
    {
        DllPath   = dllPath;
        SharpPath = sharpPath;
        Version   = version;
        HotReload = hotReload;
        Debug     = debug;
        Module    = module;

        SharedSystem       = sharedSystem;
        SharpModuleManager = sharedSystem.GetSharpModuleManager();

        ClientManager = sharedSystem.GetClientManager();
        ModSharp      = sharedSystem.GetModSharp();
        LoggerFactory = sharedSystem.GetLoggerFactory();
    }

    internal void ResolveOptionalModules()
    {
        AdminManager ??= SharpModuleManager
            .GetOptionalSharpModuleInterface<IAdminManager>(IAdminManager.Identity)?.Instance;

        ClientPreferences ??= SharpModuleManager
            .GetOptionalSharpModuleInterface<IClientPreference>(IClientPreference.Identity)?.Instance;
    }
}
