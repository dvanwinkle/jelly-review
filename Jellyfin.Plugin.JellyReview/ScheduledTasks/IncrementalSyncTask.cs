using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyReview.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyReview.ScheduledTasks;

public class IncrementalSyncTask : IScheduledTask
{
    private readonly SyncService _syncService;
    private readonly RuleEngine _ruleEngine;
    private readonly ILogger<IncrementalSyncTask> _logger;

    public IncrementalSyncTask(
        SyncService syncService,
        RuleEngine ruleEngine,
        ILogger<IncrementalSyncTask> logger)
    {
        _syncService = syncService;
        _ruleEngine = ruleEngine;
        _logger = logger;
    }

    public string Name => "JellyReview: Incremental Sync";
    public string Key => "JellyReviewIncrementalSync";
    public string Description => "Syncs recently added media items and applies pending tags for review.";
    public string Category => "JellyReview";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => new[]
    {
        new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromMinutes(5).Ticks
        }
    };

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("JellyReview: starting incremental sync");
        progress.Report(0);

        // Seed starter rules on first run if none exist
        await _ruleEngine.SeedStarterRulesAsync().ConfigureAwait(false);
        progress.Report(10);

        var result = await _syncService.RunIncrementalSyncAsync(cancellationToken).ConfigureAwait(false);
        progress.Report(100);

        _logger.LogInformation(
            "JellyReview: incremental sync complete — imported={Imported} errors={Errors}",
            result.Imported, result.Errors);
    }
}
