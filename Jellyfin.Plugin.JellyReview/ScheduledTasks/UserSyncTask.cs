using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyReview.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyReview.ScheduledTasks;

public class UserSyncTask : IScheduledTask
{
    private readonly UserSyncService _userSyncService;
    private readonly ILogger<UserSyncTask> _logger;

    public UserSyncTask(UserSyncService userSyncService, ILogger<UserSyncTask> logger)
    {
        _userSyncService = userSyncService;
        _logger = logger;
    }

    public string Name => "JellyReview: User Sync";
    public string Key => "JellyReviewUserSync";
    public string Description => "Deactivates viewer profiles for deleted Jellyfin users and removes them from rules.";
    public string Category => "JellyReview";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => new[]
    {
        new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.DailyTrigger,
            TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
        }
    };

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("JellyReview: starting user sync");
        progress.Report(0);

        var result = await _userSyncService.SyncDeletedUsersAsync().ConfigureAwait(false);
        progress.Report(100);

        _logger.LogInformation(
            "JellyReview: user sync complete — deactivatedProfiles={Profiles} deletedRules={Rules}",
            result.DeactivatedProfiles, result.DeletedRules);
    }
}
