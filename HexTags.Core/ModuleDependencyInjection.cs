using HexTags.Core.Configuration;
using HexTags.Core.Modules;
using Microsoft.Extensions.DependencyInjection;
using Sharp.Extensions.GameEventManager;

namespace HexTags.Core;

internal static class ModuleDependencyInjection
{
    internal static IServiceCollection AddModuleDi(this IServiceCollection services)
    {
        services.AddGameEventManager();

        services.AddSingleton(sp =>
        {
            var lf = sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>();
            return HexTagsConfig.Load(
                sp.GetRequiredService<InterfaceBridge>().SharpPath,
                lf.CreateLogger(nameof(HexTagsConfig)));
        });

        services.AddSingleton<TagResolverModule>();
        services.AddSingleton<ChatHookModule>();
        services.AddSingleton<ScoreboardTagModule>();

        services.AddSingleton<IModule>(sp => sp.GetRequiredService<TagResolverModule>());
        services.AddSingleton<IModule>(sp => sp.GetRequiredService<ChatHookModule>());
        services.AddSingleton<IModule>(sp => sp.GetRequiredService<ScoreboardTagModule>());

        return services;
    }
}
