using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sharp.Shared;

namespace HexTags.Core;

public sealed class HexTagsPlugin : IModSharpModule
{
    public string DisplayName   => "HexTags";
    public string DisplayAuthor => "yappershq";

    private readonly ServiceProvider        _serviceProvider;
    private readonly ILogger<HexTagsPlugin> _logger;
    private readonly InterfaceBridge        _bridge;

    public HexTagsPlugin(
        ISharedSystem  sharedSystem,
        string         dllPath,
        string         sharpPath,
        Version        version,
        IConfiguration configuration,
        bool           hotReload)
    {
        var loggerFactory = sharedSystem.GetLoggerFactory();
        _logger = loggerFactory.CreateLogger<HexTagsPlugin>();

        _bridge = new InterfaceBridge(
            dllPath, sharpPath, version, sharedSystem, this, hotReload,
            sharedSystem.GetModSharp().HasCommandLine("-debug"));

        var services = new ServiceCollection();
        services.AddSingleton(_bridge);
        services.AddSingleton(loggerFactory);
        services.AddSingleton(sharedSystem);
        services.AddLogging();
        services.AddModuleDi();

        _serviceProvider = services.BuildServiceProvider();
    }

    public bool Init()
    {
        foreach (var module in _serviceProvider.GetServices<IModule>())
        {
            try
            {
                if (module.Init()) continue;
                _logger.LogError("[HexTags] Init failed for {Module}", module.GetType().FullName);
                return false;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "[HexTags] Init threw in {Module}", module.GetType().FullName);
                return false;
            }
        }
        return true;
    }

    public void PostInit()
    {
        foreach (var module in _serviceProvider.GetServices<IModule>())
        {
            try { module.OnPostInit(_serviceProvider); }
            catch (Exception e) { _logger.LogError(e, "[HexTags] PostInit threw in {Module}", module.GetType().FullName); }
        }
    }

    public void OnAllModulesLoaded()
    {
        _bridge.ResolveOptionalModules();

        foreach (var module in _serviceProvider.GetServices<IModule>())
        {
            try { module.OnAllModulesLoaded(_serviceProvider); }
            catch (Exception e) { _logger.LogError(e, "[HexTags] OAM threw in {Module}", module.GetType().FullName); }
        }

        _logger.LogInformation("[HexTags] Loaded (Admin={Adm})", _bridge.AdminManager is not null);
    }

    public void Shutdown()
    {
        foreach (var module in _serviceProvider.GetServices<IModule>())
        {
            try { module.Shutdown(); }
            catch (Exception e) { _logger.LogError(e, "[HexTags] Shutdown threw in {Module}", module.GetType().FullName); }
        }
        _serviceProvider.Dispose();
    }
}
