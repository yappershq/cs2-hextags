using System;
using ChatProcessor.Shared;
using ChatProcessor.Shared.Models;
using HexTags.Core.Utils;
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

            // Apply tag + name color to the Name field.
            // Result: <NameColor><Tag><OriginalName><reset>
            var nameColor = ChatFormat.ProcessColorCodes(tag.NameColor);
            var prefix    = ChatFormat.ProcessColorCodes(tag.Tag);
            var reset     = ChatFormat.ProcessColorCodes("{default}");

            if (!string.IsNullOrEmpty(prefix) || !string.IsNullOrEmpty(nameColor))
                msg.Name = $"{nameColor}{prefix}{msg.Name}{reset}";

            // Apply chat color to the message itself.
            if (!string.IsNullOrEmpty(tag.ChatColor))
            {
                var chatColor = ChatFormat.ProcessColorCodes(tag.ChatColor);
                msg.Message = $"{chatColor}{msg.Message}";
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "[HexTags] PreProcess handler threw for SteamId={SteamId}", msg.SteamId);
        }
    }
}
