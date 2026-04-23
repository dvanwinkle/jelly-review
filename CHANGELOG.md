# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).

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
