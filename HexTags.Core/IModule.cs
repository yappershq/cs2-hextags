using Microsoft.Extensions.DependencyInjection;

namespace HexTags.Core;

internal interface IModule
{
    bool Init();
    void OnPostInit(ServiceProvider provider) { }
    void OnAllModulesLoaded(ServiceProvider provider) { }
    void Shutdown() { }
}
