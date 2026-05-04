# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.1] - 2026-05-03

### Added

- Add `UserSyncService` that deactivates viewer profiles whose Jellyfin user no longer exists and removes them from any associated rules, deleting rules entirely when all their profiles are orphaned.
- Add `UserSyncTask` scheduled task (daily at 3 AM) to run the user sync automatically.
- Add `UserEventListener` that triggers a user sync whenever Jellyfin fires `OnUserUpdated`, providing near-immediate cleanup when users are modified or removed.

### Fixed

- Filter the Add Rule profiles dropdown to only show profiles linked to an active Jellyfin user, preventing orphaned profiles from appearing as rule targets.

## [0.1.0] - 2026-04-29

### Added

- Add per-profile review decisions, tag enforcement, dashboard filtering, and many-to-many rule assignments.
- Add missing Jellyfin plugin template scaffolding, including `jellyfin.ruleset`, Renovate configuration, VS Code workspace tasks, and CI workflow files.
- Add an approved-item allow tag and configure viewer profiles with Jellyfin's allowed-tags parental control.

### Changed

- Disable the Jellyfin upstream publish workflow until deploy secrets are configured.
- Remove the reusable Jellyfin changelog workflow in favor of the existing explicit `CHANGELOG.md` and tag release flow.

### Fixed

- Store JellyReview parental-control tags as separate Jellyfin preference values instead of a pipe-delimited tag.
- Correct review action toast text so deny actions display "Denied successfully" instead of "Denyd successfully".

## [0.0.4] - 2026-04-25

### Fixed

- Align JellyReview configuration pages with Jellyfin's native plugin page layout to prevent collisions with the dashboard navigation bar.

### Changed

- Centralize plugin release metadata in `Directory.Build.props` and `build.yaml`.
- Add a solution file for C# IDE tooling.
- Generate plugin repository manifest versions from newest to oldest.

## [0.0.3] - 2026-04-25

### Fixed

- Reapply automatic review rules after Jellyfin metadata update events so newly imported movies are evaluated once ratings and genres are available.
- Preserve manual review decisions while allowing prior automatic import decisions to be updated by later sync passes.

## [0.0.2] - 2026-04-23

### Added

- Visual rule builder with chip-based selectors for ratings, media types, and genres
- Number inputs for community rating threshold conditions
- Mutual exclusion between "rating is" and "rating is NOT" conditions
- Inline rule editing via Edit button on each rule row
- Human-readable condition display in rule list
- Cancel button to discard in-progress edits
- Live JSON preview while building conditions
- Extracted rule-builder module with 34 unit tests (vitest)
- JavaScript test step in CI workflow

### Changed

- Plugin manifest now includes all historical versions instead of only the latest

## [0.0.1] - 2026-04-23

### Added

- Initial release
- Review queue with approve, deny, defer, and reopen actions
- Bulk review operations
- Auto-review rule engine with rating, genre, media type, and community score conditions
- Three default starter rules (family-friendly auto-approve, teen manual review, adult auto-deny)
- Viewer profiles with automatic blocked tag configuration
- Full decision audit trail
- Real-time library event detection and incremental sync
- Notification providers: Discord, Slack, Email (SMTP), Ntfy, Pushover, Webhook
- HMAC-signed action tokens for approve/deny from notifications
- Embedded dashboard with queue, history, profiles, rules, notifications, and settings tabs
- Plugin repository manifest via GitHub Pages

[Unreleased]: https://github.com/dvanwinkle/jelly-review/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/dvanwinkle/jelly-review/compare/v0.0.4...v0.1.0
[0.0.4]: https://github.com/dvanwinkle/jelly-review/compare/v0.0.3...v0.0.4
[0.0.3]: https://github.com/dvanwinkle/jelly-review/compare/v0.0.2...v0.0.3
[0.0.2]: https://github.com/dvanwinkle/jelly-review/compare/v0.0.1...v0.0.2
[0.0.1]: https://github.com/dvanwinkle/jelly-review/releases/tag/v0.0.1
