# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).

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
