# JellyReview

A parental media review plugin for [Jellyfin](https://jellyfin.org). When new movies or series are added to your library, JellyReview automatically holds them for parent approval before children can access them — using Jellyfin's built-in tag-based parental controls.

## How It Works

1. New media is added to Jellyfin.
2. JellyReview tags it `jelly-guard-pending`, which blocks it for child users.
3. A parent reviews the item from the built-in dashboard (approve, deny, or defer).
4. On approval the tag is removed and children can watch. On denial a `jelly-guard-denied` tag is applied.
5. Notifications are sent to parents when items need review.

Auto-review rules can handle common cases automatically — for example, auto-approving G-rated content and auto-denying R-rated content.

## Features

- **Review queue** with approve/deny/defer actions and bulk operations
- **Auto-review rules** based on rating, genre, community score, and media type
- **Viewer profiles** that map Jellyfin users to children and configure their blocked tags automatically
- **Six notification providers**: Discord, Slack, Email (SMTP), Ntfy, Pushover, and generic Webhook
- **Action tokens** — approve or deny directly from a notification link (HMAC-signed, 72-hour expiry)
- **Full audit trail** of every review decision
- **Incremental sync** every 5 minutes plus real-time detection of newly added items
- **No external dependencies** — runs entirely inside Jellyfin with an embedded SQLite database

## Installation

### From Plugin Repository (Recommended)

1. In Jellyfin, go to **Dashboard → Plugins → Repositories**.
2. Add a new repository with the URL:
   ```
   https://dvanwinkle.github.io/jelly-review/manifest.json
   ```
3. Go to **Catalog**, find **JellyReview**, and install it.
4. Restart Jellyfin.

### Manual Install

1. Download `Jellyfin.Plugin.JellyReview.zip` from the [latest release](../../releases/latest).
2. Extract it into your Jellyfin plugins directory (e.g. `config/plugins/JellyReview/`).
3. Restart Jellyfin.

## Setup

1. Open the **JellyReview** page from the Jellyfin sidebar (visible to admin users only).
2. Go to the **Profiles** tab and create viewer profiles for each child user. This automatically configures blocked tags for pending/denied items and an allowed tag for approved items.
3. Optionally configure **Rules** to auto-approve or auto-deny based on content ratings.
4. Optionally add **Notification** channels to get alerted when items need review.

## Default Rules

On first run, three starter rules are created:

| Priority | Name | Action | Ratings |
|----------|------|--------|---------|
| 10 | Auto-approve safe ratings | Auto-approve | G, PG, TV-Y, TV-Y7, TV-G, TV-PG |
| 20 | Send teen ratings to manual review | Send to review | PG-13, TV-14 |
| 30 | Auto-deny adult content | Auto-deny | R, NC-17, TV-MA, X |

Rules are fully customizable from the dashboard.

## Rule Conditions

Rules support the following conditions (all must match):

| Condition | Description |
|-----------|-------------|
| `official_rating_in` | Item rating must be one of these values |
| `official_rating_not_in` | Item rating must not be any of these values |
| `media_type_in` | Must be `"movie"` or `"series"` |
| `community_rating_gte` | Minimum community rating |
| `community_rating_lte` | Maximum community rating |
| `genre_in` | Item must have at least one of these genres |

## Notification Providers

| Provider | Description |
|----------|-------------|
| **Discord** | Rich embeds via webhook with poster thumbnails and color-coded ratings |
| **Slack** | Block Kit messages with action buttons |
| **Email (SMTP)** | HTML emails with styled action buttons |
| **Ntfy** | Push notifications with approve/deny actions |
| **Pushover** | Mobile push with deep link to the dashboard |
| **Webhook** | Generic JSON webhook with optional secret header |

## API

All endpoints are under `/JellyReview/` and require admin authentication unless noted.

| Endpoint | Description |
|----------|-------------|
| `GET /Media` | List media with state/search filters and pagination |
| `GET /Media/counts` | Counts by review state |
| `POST /Media/sync` | Trigger incremental or full sync |
| `POST /Reviews/{itemId}/approve` | Approve an item |
| `POST /Reviews/{itemId}/deny` | Deny an item |
| `POST /Reviews/{itemId}/defer` | Defer an item |
| `POST /Reviews/bulk` | Bulk actions |
| `GET /Reviews/{itemId}/history` | Decision audit trail |
| `GET /Reviews/viewer-profiles` | List viewer profiles |
| `POST /Reviews/viewer-profiles` | Create a viewer profile |
| `GET /Rules` | List auto-review rules |
| `POST /Rules` | Create a rule |
| `GET /Notifications/channels` | List notification channels |
| `POST /Notifications/channels` | Create a channel |
| `POST /Notifications/channels/{id}/test` | Send a test notification |
| `GET /Settings` | Get plugin settings |
| `GET /System/status` | Plugin version, counts, last sync time |
| `POST /Action/{token}` | Execute action from notification link (anonymous) |

## Building from Source

```bash
cd Jellyfin.Plugin.JellyReview
dotnet build --configuration Release
dotnet publish --configuration Release --output ../publish
```

The published output can be zipped and placed in Jellyfin's plugin directory.

## Requirements

- Jellyfin 10.10+
- .NET 9.0 runtime (provided by Jellyfin)

## License

This project is licensed under the [MIT License](LICENSE).
