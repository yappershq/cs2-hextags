using System;
using System.Threading;
using System.Threading.Tasks;
using HexTags.Core.Configuration;
using HexTags.Core.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HexTags.Core.Modules;

/// <summary>
///     Sources tag rules from the shared MySQL DB. JSON stays the offline fallback
///     and the initial seed. Polls a version marker; only reloads when it changes.
///     All DB work runs off the game thread; live rule swaps go through
///     <see cref="TagResolverModule.ReplaceRules"/> (volatile swap + cache clear).
/// </summary>
internal sealed class RuleSyncModule : IModule
{
    private readonly HexTagsConfig            _config;
    private readonly TagResolverModule        _resolver;
    private readonly ILogger<RuleSyncModule>  _logger;
    private readonly HexTagsDb                _db;

    private Timer? _timer;
    private long   _lastVersion;
    private int    _polling; // 0/1 guard so overlapping ticks don't stack

    public RuleSyncModule(HexTagsConfig config, TagResolverModule resolver, ILogger<RuleSyncModule> logger)
    {
        _config   = config;
        _resolver = resolver;
        _logger   = logger;
        _db       = new HexTagsDb(logger);
    }

    public bool Init() => true;

    public void OnAllModulesLoaded(ServiceProvider provider)
    {
        if (!_config.UseDatabase || string.IsNullOrWhiteSpace(_config.Database.Host))
        {
            _logger.LogInformation("[HexTags] DB rule source disabled, using JSON");
            return;
        }

        if (!_db.Connect(_config.Database))
        {
            _logger.LogWarning("[HexTags] DB unavailable, keeping JSON rules");
            return;
        }

        // OAM can't be async; do the initial load on a Task. The timer also covers
        // it, so even if this races a tick the version guard keeps it idempotent.
        _ = Task.Run(InitialLoadAsync);

        var interval = Math.Max(15, _config.PollSeconds);
        _timer = new Timer(_ => Poll(), null,
            TimeSpan.FromSeconds(interval), TimeSpan.FromSeconds(interval));
        _logger.LogInformation("[HexTags] DB rule polling every {Sec}s (ServerTag='{Tag}')",
            interval, _config.ServerTag);
    }

    private async Task InitialLoadAsync()
    {
        try
        {
            await _db.EnsureSchemaAsync();

            if (await _db.CountRulesAsync() == 0)
            {
                await _db.SeedFromRulesAsync(_config.Rules);
                _logger.LogInformation("[HexTags] Seeded {Count} JSON rules into empty DB", _config.Rules.Count);
            }

            var rules = await _db.LoadRulesAsync(_config.ServerTag);
            if (rules.Count > 0)
            {
                _resolver.ReplaceRules(rules);
                _logger.LogInformation("[HexTags] Loaded {Count} rules from DB", rules.Count);
            }
            else
            {
                _logger.LogWarning("[HexTags] DB returned 0 rules, keeping JSON fallback");
            }

            _lastVersion = await _db.GetVersionAsync();
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "[HexTags] Initial DB rule load failed, keeping JSON");
        }
    }

    private void Poll()
    {
        // Skip if a prior tick (or the initial load) is still running.
        if (Interlocked.CompareExchange(ref _polling, 1, 0) != 0)
            return;

        _ = PollAsync();
    }

    private async Task PollAsync()
    {
        try
        {
            var version = await _db.GetVersionAsync();
            if (version == _lastVersion)
                return;

            var rules = await _db.LoadRulesAsync(_config.ServerTag);
            if (rules.Count > 0)
            {
                _resolver.ReplaceRules(rules);
                _lastVersion = version;
                _logger.LogInformation("[HexTags] Rules reloaded from DB, v{Version} ({Count} rules)",
                    version, rules.Count);
            }
            else
            {
                _logger.LogWarning("[HexTags] DB version changed to v{Version} but returned 0 rules; keeping current",
                    version);
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "[HexTags] Rule poll failed");
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    public void Shutdown()
    {
        _timer?.Dispose();
        _timer = null;
        _db.Dispose();
    }
}
