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
