# jelly-review: Jellyfin Parental Review Plugin — Implementation Plan

## Overview

A native Jellyfin server plugin (C# .NET) that replaces the existing jelly-guard FastAPI + Next.js stack. Runs fully inside Jellyfin — no separate service or proxy required.

### Core workflow
1. New media item added to Jellyfin library
2. Plugin tags it `jelly-guard-pending` via `ILibraryManager`
3. Jellyfin's built-in parental controls block that tag for child users
4. Parent (Jellyfin admin) reviews via the plugin's sidebar dashboard
5. Approval → tag removed → child can see/play the item
6. Denial → `jelly-guard-denied` tag applied
7. Notifications sent to parents when items need review

### Authorization model
- Parents = Jellyfin admin users ("Allow this user to manage the server" can remain unchecked)
- Children = regular non-admin Jellyfin users
- Plugin sidebar is automatically hidden from non-admins (Jellyfin gates `GET /web/ConfigurationPages` behind `Policies.RequiresElevation`)
- All plugin API endpoints use `[Authorize(Policy = Policies.RequiresElevation)]`

### Known limitation
Per-child viewer decisions are stored in the DB for audit purposes, but Jellyfin's tag-based parental controls are global per item — not per user. Full per-child enforcement at the tag layer isn't natively possible without custom Jellyfin middleware. The global `jelly-guard-pending` tag blocks all children until a parent approves.

---

## Project Structure

```
Jellyfin.Plugin.JellyReview/
  Jellyfin.Plugin.JellyReview.csproj
  Plugin.cs                        # BasePlugin<PluginConfiguration>, IHasWebPages
  PluginServiceRegistrator.cs      # IPluginServiceRegistrator — DI registration

  Configuration/
    PluginConfiguration.cs         # XML config: tag names, polling interval, library IDs

  Data/
    DatabaseManager.cs             # SQLite init, connection factory, write lock
    Schema.sql                     # Embedded CREATE TABLE statements

  Models/
    MediaRecord.cs
    ReviewDecision.cs
    ViewerDecision.cs
    DecisionHistory.cs
    ViewerProfile.cs
    ReviewRule.cs
    NotificationChannel.cs
    NotificationDelivery.cs
    SyncCursor.cs

  Services/
    ReviewService.cs               # State machine: approve/deny/defer/reopen
    RuleEngine.cs                  # Condition evaluation, rating normalization
    SyncService.cs                 # Library import and incremental sync
    TagManager.cs                  # ILibraryManager tag ops with re-entrancy guard
    NotificationService.cs         # Dispatch to providers
    Notifications/
      INotificationProvider.cs
      ReviewNotificationPayload.cs
      DiscordProvider.cs
      NtfyProvider.cs
      PushoverProvider.cs
      SmtpProvider.cs
      SlackProvider.cs
      WebhookProvider.cs

  ScheduledTasks/
    LibraryEventListener.cs        # IHostedService — ItemAdded/ItemUpdated events
    IncrementalSyncTask.cs         # IScheduledTask — visible in Jellyfin dashboard

  Api/
    ReviewController.cs
    MediaController.cs
    RulesController.cs
    NotificationsController.cs
    SettingsController.cs
    SystemController.cs
    ActionTokenController.cs       # [AllowAnonymous] — HMAC token actions
    Dtos/                          # Request/response DTOs

  Web/
    Pages/
      dashboard.html               # Embedded review dashboard (vanilla JS)
      config.html                  # Standard Jellyfin plugin config page
    Scripts/
      api.js                       # Thin fetch wrapper
      dashboard.js
      config.js

  build.yaml                       # GitHub Actions build
```

---

## Build Sequence

Build in this order — each step is testable before moving on.

1. **Scaffold** from the [jellyfin-plugin-template](https://github.com/jellyfin/jellyfin-plugin-template). Rename to `Jellyfin.Plugin.JellyReview`. Add `Microsoft.Data.Sqlite` package reference.

2. **`Plugin.cs`** + empty `config.html` → confirm plugin loads in Jellyfin sidebar and Plugins list.

3. **`DatabaseManager`** + `Schema.sql` → confirm DB file created at `{DataPath}/jellyreview.db` on startup.

4. **`LibraryEventListener`** (stubs) → confirm `ItemAdded` event fires when media is added.

5. **`TagManager`** with re-entrancy guard → confirm `jelly-guard-pending` tag appears on new items without infinite loop.

6. **`RuleEngine`** → pure logic, port directly from jelly-guard's `rules.py`. Unit-testable in isolation.

7. **`ReviewService`** → state transitions + history, port from jelly-guard's `review.py`.

8. **`ReviewController`** → test approve/deny/defer via curl or Swagger.

9. **`SyncService`** full import + incremental → test `POST /JellyReview/Media/sync`.

10. **`IncrementalSyncTask`** → verify it appears in Jellyfin Dashboard → Scheduled Tasks.

11. **`NotificationService`** + Discord first → verify end-to-end notification delivery.

12. Remaining notification providers (Ntfy, Pushover, SMTP, Slack, Webhook).

13. **`ActionTokenController`** + HMAC token → test approve-from-notification-link.

14. Remaining controllers (`MediaController`, `RulesController`, `SettingsController`).

15. **Dashboard HTML/JS** → iterate against working API endpoints.

16. **Build + zip** → test `.zip` install in a clean Jellyfin instance.

---

## Component Details

### `Plugin.cs`

```csharp
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static Plugin Instance { get; private set; } = null!;

    public Plugin(IApplicationPaths paths, IXmlSerializer serializer)
        : base(paths, serializer) { Instance = this; }

    public override string Name => "JellyReview";
    public override Guid Id => Guid.Parse("your-stable-guid-here"); // generate once, never change

    public IEnumerable<PluginPageInfo> GetPages() => new[]
    {
        new PluginPageInfo
        {
            Name = "JellyReview",
            EmbeddedResourcePath = $"{GetType().Namespace}.Web.Pages.dashboard.html",
            EnableInMainMenu = true,
            DisplayName = "JellyReview"
        },
        new PluginPageInfo
        {
            Name = "JellyReviewConfig",
            EmbeddedResourcePath = $"{GetType().Namespace}.Web.Pages.config.html"
        }
    };
}
```

`Plugin.cs` must **not** implement `IHostedService` — Jellyfin restriction.

### `PluginConfiguration.cs`

Properties (XML-serialized, non-sensitive only):
- `PendingTag` — default `"jelly-guard-pending"`
- `DeniedTag` — default `"jelly-guard-denied"`
- `PollingIntervalSeconds` — default `300`
- `AutoRulesEnabled` — default `true`
- `SelectedLibraryIds` — `string` (JSON array of library GUIDs)

Notification channel credentials go in SQLite (same filesystem permissions as Jellyfin's own secrets), not in the XML config.

### `PluginServiceRegistrator.cs`

```csharp
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<DatabaseManager>();
        serviceCollection.AddSingleton<TagManager>();
        serviceCollection.AddSingleton<ReviewService>();
        serviceCollection.AddSingleton<RuleEngine>();
        serviceCollection.AddSingleton<SyncService>();
        serviceCollection.AddSingleton<NotificationService>();
        serviceCollection.AddHostedService<LibraryEventListener>();
        serviceCollection.AddScoped<IncrementalSyncTask>(); // Jellyfin creates per-run
    }
}
```

### `DatabaseManager.cs`

- Path: `IApplicationPaths.DataPath + "/jellyreview.db"`
- Use `Microsoft.Data.Sqlite` directly (no ORM)
- On construction, run `Schema.sql` from embedded resources with `CREATE TABLE IF NOT EXISTS`
- Expose `GetConnection()` returning an open `SqliteConnection`
- Protect concurrent writes with `SemaphoreSlim(1, 1)`
- Connection string: `Mode=ReadWriteCreate;Cache=Shared`

### `Schema.sql` (embedded resource)

```sql
CREATE TABLE IF NOT EXISTS media_records (
    id TEXT PRIMARY KEY,
    jellyfin_item_id TEXT UNIQUE NOT NULL,
    media_type TEXT NOT NULL,
    title TEXT NOT NULL,
    sort_title TEXT,
    year INTEGER,
    official_rating TEXT,
    community_rating REAL,
    runtime_minutes INTEGER,
    overview TEXT,
    genres_json TEXT,
    tags_snapshot_json TEXT,
    status TEXT NOT NULL DEFAULT 'active',
    metadata_hash TEXT,
    first_seen_at TEXT NOT NULL DEFAULT (datetime('now')),
    last_seen_at TEXT NOT NULL DEFAULT (datetime('now')),
    last_synced_at TEXT
);

CREATE TABLE IF NOT EXISTS review_decisions (
    id TEXT PRIMARY KEY,
    media_record_id TEXT UNIQUE NOT NULL REFERENCES media_records(id),
    state TEXT NOT NULL DEFAULT 'pending',
    decision_reason TEXT,
    reviewer_jellyfin_user_id TEXT,
    reviewed_at TEXT,
    source TEXT NOT NULL DEFAULT 'sync_reconciliation',
    notes TEXT,
    needs_resync INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS viewer_profiles (
    id TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    jellyfin_user_id TEXT,
    age_hint INTEGER,
    active_rule_set_id TEXT,
    created_by_jellyfin_user_id TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS viewer_decisions (
    id TEXT PRIMARY KEY,
    viewer_profile_id TEXT NOT NULL REFERENCES viewer_profiles(id),
    media_record_id TEXT NOT NULL REFERENCES media_records(id),
    state TEXT NOT NULL DEFAULT 'pending',
    decision_reason TEXT,
    reviewer_jellyfin_user_id TEXT,
    reviewed_at TEXT,
    source TEXT NOT NULL DEFAULT 'manual_review',
    notes TEXT,
    needs_resync INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at TEXT NOT NULL DEFAULT (datetime('now')),
    UNIQUE(viewer_profile_id, media_record_id)
);

CREATE TABLE IF NOT EXISTS decision_history (
    id TEXT PRIMARY KEY,
    media_record_id TEXT NOT NULL,
    viewer_profile_id TEXT,
    previous_state TEXT,
    new_state TEXT NOT NULL,
    action TEXT NOT NULL,
    actor_type TEXT NOT NULL,
    actor_id TEXT,
    details_json TEXT,
    created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS review_rules (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    enabled INTEGER NOT NULL DEFAULT 1,
    priority INTEGER NOT NULL DEFAULT 100,
    conditions_json TEXT NOT NULL,
    action TEXT NOT NULL,
    viewer_profile_id TEXT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS notification_channels (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    provider_type TEXT NOT NULL,
    config_json TEXT,
    enabled INTEGER NOT NULL DEFAULT 1,
    notify_on_pending INTEGER NOT NULL DEFAULT 1,
    notify_on_conflict INTEGER NOT NULL DEFAULT 1,
    notify_on_digest INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS notification_deliveries (
    id TEXT PRIMARY KEY,
    channel_id TEXT NOT NULL,
    media_record_id TEXT,
    trigger_event TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'pending',
    provider_message_id TEXT,
    action_token_hash TEXT,
    actioned_at TEXT,
    error_detail TEXT,
    created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS sync_cursors (
    id TEXT PRIMARY KEY,
    source TEXT UNIQUE NOT NULL,
    cursor_value TEXT,
    last_run_at TEXT,
    last_success_at TEXT,
    error_count INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS app_secrets (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);
```

No `AppUser` table — authentication is delegated entirely to Jellyfin. `reviewer_jellyfin_user_id` columns store Jellyfin user GUIDs, looked up via `IUserManager` when display names are needed.

### `TagManager.cs`

The re-entrancy guard is the most critical piece. `UpdateItemAsync` re-triggers `ItemAdded` — without the guard you get an infinite loop:

```csharp
private static readonly HashSet<Guid> _tagWriteInFlight = new();
private static readonly SemaphoreSlim _tagLock = new(1, 1);

public async Task ApplyPendingTagAsync(Guid itemId)
{
    await _tagLock.WaitAsync();
    try
    {
        if (_tagWriteInFlight.Contains(itemId)) return;
        _tagWriteInFlight.Add(itemId);
    }
    finally { _tagLock.Release(); }

    try
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item == null) return;
        var tags = item.Tags?.ToList() ?? new List<string>();
        if (!tags.Contains(_config.PendingTag))
        {
            tags.Add(_config.PendingTag);
            item.Tags = tags.ToArray();
            await _libraryManager.UpdateItemAsync(
                item, item.GetParent(), ItemUpdateType.MetadataEdit, CancellationToken.None);
        }
    }
    finally
    {
        await _tagLock.WaitAsync();
        _tagWriteInFlight.Remove(itemId);
        _tagLock.Release();
    }
}

public bool IsTagWriteInFlight(Guid itemId)
{
    _tagLock.Wait();
    try { return _tagWriteInFlight.Contains(itemId); }
    finally { _tagLock.Release(); }
}
```

`ApplyDecisionTagsAsync` state → tag mapping:
- `approved` → remove `PendingTag`, remove `DeniedTag`
- `denied` → remove `PendingTag`, add `DeniedTag`
- `pending` → add `PendingTag`, remove `DeniedTag`
- `deferred` → keep `PendingTag` (child cannot see until final decision)

### `LibraryEventListener.cs`

```csharp
public class LibraryEventListener : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemAdded;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        return Task.CompletedTask;
    }

    private async void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        // Only top-level items — Jellyfin fires this for every Episode too
        if (e.Item is not (Movie or Series)) return;
        // Skip our own tag writes
        if (_tagManager.IsTagWriteInFlight(e.Item.Id)) return;

        try
        {
            await _syncService.HandleNewItemAsync(e.Item);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JellyReview: unhandled error processing ItemAdded for {ItemId}", e.Item.Id);
        }
    }
}
```

Must use `async void` — Jellyfin's event does not await handlers. Always wrap in try/catch.

### `IncrementalSyncTask.cs`

```csharp
public class IncrementalSyncTask : IScheduledTask
{
    public string Name => "JellyReview: Incremental Sync";
    public string Key => "JellyReviewIncrementalSync";
    public string Description => "Sync recently added items and apply pending tags.";
    public string Category => "JellyReview";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => new[]
    {
        new TaskTriggerInfo
        {
            Type = TaskTriggerInfo.TriggerInterval,
            IntervalTicks = TimeSpan.FromMinutes(5).Ticks
        }
    };

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => await _syncService.RunIncrementalSyncAsync(cancellationToken);
}
```

### `RuleEngine.cs`

Port of jelly-guard's `rules.py`. Key details:
- `EvaluateAsync(MediaRecord, viewerProfileId?)` returns `(RuleAction?, ReviewRule?)`
- Profile-scoped rules evaluated before global rules, both ordered by `priority ASC`
- Port `normalize_official_rating` exactly (strip, uppercase, replace spaces/underscores with hyphens, collapse double hyphens)
- Seed starter rules on first use (check for zero rows, insert the same three starters)
- Supported conditions: `official_rating_in`, `official_rating_not_in`, `media_type_in`, `community_rating_gte`, `community_rating_lte`, `genre_in`

### `ReviewService.cs`

Port of jelly-guard's `review.py`. State machine:
- `approve` → `approved`
- `deny` → `denied`
- `defer` → `deferred`
- `reopen` → `pending`

On every transition: write `DecisionHistory` row, update decision record, call `TagManager.ApplyDecisionTagsAsync()`.

### `SyncService.cs`

Port of jelly-guard's `sync.py`. Key methods:

**`HandleNewItemAsync(BaseItem)`** — called by `LibraryEventListener`. Reads item's existing tags (migration path), runs `RuleEngine.EvaluateAsync()`, upserts `MediaRecord`, creates `ReviewDecision`, calls `TagManager.ApplyPendingTagAsync()` if pending, fires notification.

**`RunIncrementalSyncAsync()`** — called by `IncrementalSyncTask`. Queries `_libraryManager.QueryItems()` filtering by `DateCreated > cursor.last_success_at`. Calls `HandleNewItemAsync` for items not in DB. Updates cursor.

**`RunFullImportAsync()`** — admin-triggered via `POST /JellyReview/Media/sync`. Enumerates all `Movie` and `Series` from selected libraries via `_libraryManager.QueryItems(new InternalItemsQuery { IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series }, Recursive = true })`.

Never call `localhost:8096` from inside the plugin — use `ILibraryManager` directly.

### Action Tokens

Replace Python's `itsdangerous` with `HMACSHA256`:

Format: `base64url({mediaRecordId}|{action}|{viewerProfileId}|{issuedAtUnix})` + `.` + `base64url(HMAC-SHA256)`

- 32-byte signing key stored in `app_secrets` table, generated once on first use
- Verify: recompute HMAC, compare in constant time, check `issuedAt + maxAgeSeconds > now`
- Default expiry: 72 hours
- `ActionTokenController` uses `[AllowAnonymous]` but validates token before executing any action

### API Routes

All routes prefixed with `/JellyReview/` (never `/api/` — Jellyfin owns that prefix).
All controllers use `[Authorize(Policy = Policies.RequiresElevation)]` except `ActionTokenController`.

```
ReviewController:
  POST /JellyReview/Reviews/{itemId}/approve
  POST /JellyReview/Reviews/{itemId}/deny
  POST /JellyReview/Reviews/{itemId}/defer
  POST /JellyReview/Reviews/{itemId}/reopen
  POST /JellyReview/Reviews/bulk
  GET  /JellyReview/Reviews/{itemId}/history
  GET  /JellyReview/Reviews/viewer-profiles
  POST /JellyReview/Reviews/viewer-profiles

MediaController:
  GET  /JellyReview/Media
  GET  /JellyReview/Media/counts
  GET  /JellyReview/Media/{itemId}
  GET  /JellyReview/Media/{itemId}/poster  (redirect to Jellyfin's own image service)
  POST /JellyReview/Media/sync
  POST /JellyReview/Media/reconcile

RulesController:
  GET    /JellyReview/Rules
  POST   /JellyReview/Rules
  PATCH  /JellyReview/Rules/{id}
  DELETE /JellyReview/Rules/{id}
  POST   /JellyReview/Rules/evaluate

NotificationsController:
  GET    /JellyReview/Notifications/channels
  POST   /JellyReview/Notifications/channels
  PATCH  /JellyReview/Notifications/channels/{id}
  DELETE /JellyReview/Notifications/channels/{id}
  POST   /JellyReview/Notifications/channels/{id}/test

SettingsController:
  GET    /JellyReview/Settings
  PATCH  /JellyReview/Settings/integrations
  PATCH  /JellyReview/Settings/tags
  PATCH  /JellyReview/Settings/libraries
  GET    /JellyReview/Settings/libraries/jellyfin

ActionTokenController:
  POST   /JellyReview/Action/{token}        [AllowAnonymous]
```

### Dashboard UI

Single HTML file served as an embedded resource via `IHasWebPages`. Vanilla JS only — no build step, no framework. Jellyfin injects it into its iframe-based plugin page system. The browser already has the Jellyfin session cookie, so all `fetch()` calls to `/JellyReview/` are automatically authenticated.

Sections:
1. **Pending Queue** — card list with Approve/Deny/Defer buttons, search/filter, pagination
2. **History** — approved/denied/deferred tabs
3. **Viewer Profiles** — create/manage, assign to Jellyfin users, show setup checklist
4. **Rules** — list with toggle/priority/delete
5. **Notifications** — channel list with add/test/delete
6. **Settings** — tag names, polling interval, library selection

Include a setup checklist on the Viewer Profiles page that verifies each child user has "Block content with tags → `jelly-guard-pending`" configured in Jellyfin's user settings, and shows a warning if not.

### `.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <AssemblyName>Jellyfin.Plugin.JellyReview</AssemblyName>
    <RootNamespace>Jellyfin.Plugin.JellyReview</RootNamespace>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Jellyfin.Model" Version="10.10.*" ExcludeAssets="runtime" />
    <PackageReference Include="Jellyfin.Controller" Version="10.10.*" ExcludeAssets="runtime" />
    <PackageReference Include="Jellyfin.Common" Version="10.10.*" ExcludeAssets="runtime" />
    <PackageReference Include="Microsoft.Data.Sqlite" Version="8.*" />
    <PackageReference Include="System.Text.Json" Version="8.*" />
  </ItemGroup>

  <ItemGroup>
    <EmbeddedResource Include="Web\**\*" />
    <EmbeddedResource Include="Data\Schema.sql" />
  </ItemGroup>
</Project>
```

`ExcludeAssets="runtime"` on Jellyfin references — they are provided by the host process.

---

## Jellyfin-Specific Gotchas

| # | Gotcha | Detail |
|---|--------|--------|
| 1 | `Plugin.cs` cannot be `IHostedService` | Background services must be separate classes |
| 2 | Tag writes re-trigger `ItemAdded` | Use `_tagWriteInFlight` HashSet + check at start of `OnItemAdded` |
| 3 | Jellyfin fires `ItemAdded` for every Episode | Guard: `if (e.Item is not (Movie or Series)) return;` |
| 4 | Never call `localhost:8096` from inside the plugin | Use `ILibraryManager` directly |
| 5 | `item.Tags` is an array, not a list | `item.Tags = item.Tags.Append(tag).ToArray()` |
| 6 | `UpdateItemAsync` must use `MetadataEdit` | `None` may not propagate; `ImageUpdate` triggers re-fetch from internet |
| 7 | Route prefix must not be `/api/` | Jellyfin owns that prefix; use `/JellyReview/` |
| 8 | `EmbeddedResource` paths use dots | `Jellyfin.Plugin.JellyReview.Web.Pages.dashboard.html` |
| 9 | `async void` in event handlers | Jellyfin doesn't await them; always try/catch and log |
| 10 | `IScheduledTask` not raw timer | Appears in Jellyfin's Scheduled Tasks UI, manually triggerable |
| 11 | Series-level tagging only | Parental controls propagate from Series → Season → Episode automatically |
| 12 | `IHasWebPages` sidebar is auto-admin-gated | `GET /web/ConfigurationPages` requires elevation; no extra auth needed for the page itself |

---

## Reference: jelly-guard Source Files to Port

The existing Python implementation lives at `/Users/dvanwinkle/Developer/jelly-guard/`:

- `api/app/services/sync.py` — item import, deduplication, rule application, cursor management
- `api/app/services/review.py` — state machine, audit history, tag sync
- `api/app/services/rules.py` — rule engine, rating normalization, starter rule seeds
- `api/app/services/notifications.py` — notification dispatch, payload construction, action token generation
- `api/app/models/review.py` — data model definitions mapping to the SQLite schema
