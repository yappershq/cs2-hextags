using System;
using ChatProcessor.Shared;
using ChatProcessor.Shared.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HexTags.Core.Modules;

internal sealed class ChatHookModule : IModule
{
    private readonly InterfaceBridge            _bridge;
    private readonly ILogger<ChatHookModule>    _logger;
    private readonly TagResolverModule          _resolver;

    private IChatProcessorShared?               _chatProcessor;

    public ChatHookModule(InterfaceBridge bridge, ILogger<ChatHookModule> logger, TagResolverModule resolver)
    {
        _bridge   = bridge;
        _logger   = logger;
        _resolver = resolver;
    }

    public bool Init() => true;

    public void OnAllModulesLoaded(ServiceProvider provider)
    {
        _chatProcessor = _bridge.SharpModuleManager
            .GetOptionalSharpModuleInterface<IChatProcessorShared>(IChatProcessorShared.Identity)?.Instance;

        if (_chatProcessor is null)
        {
            _logger.LogWarning("[HexTags] ChatProcessor not found — chat tag/color injection disabled");
            return;
        }

        _chatProcessor.PreProcess += OnPreProcess;
        _logger.LogInformation("[HexTags] Subscribed to ChatProcessor.PreProcess");
    }

    public void Shutdown()
    {
        if (_chatProcessor is not null)
            _chatProcessor.PreProcess -= OnPreProcess;
    }

    private void OnPreProcess(ChatMessage msg)
    {
        try
        {
            var tag = _resolver.Resolve(msg.SteamId);

            // Write raw color tokens — ChatProcessor's SayText2 hook owns ProcessColorCodes.
            // Result: <Tag><NameColor><OriginalName><Suffix><reset>
            // NameColor must come AFTER the Tag — otherwise the Tag's trailing colour
            // overrides it and the name just bleeds the tag colour (name_color would be dead).
            // With it after the Tag: empty name_color -> name still bleeds the tag colour;
            // a set name_color actually colours the name.
            if (!string.IsNullOrEmpty(tag.Tag) || !string.IsNullOrEmpty(tag.NameColor) || !string.IsNullOrEmpty(tag.Suffix))
                msg.Name = $"{tag.Tag}{tag.NameColor}{msg.Name}{tag.Suffix}{{default}}";

            if (!string.IsNullOrEmpty(tag.ChatColor))
                msg.Message = $"{tag.ChatColor}{msg.Message}";
        }
        catch (Exception e)
        {
            _logger.LogError(e, "[HexTags] PreProcess handler threw for SteamId={SteamId}", msg.SteamId);
        }
    }
}
