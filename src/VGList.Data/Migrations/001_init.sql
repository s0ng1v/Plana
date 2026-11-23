CREATE TABLE IF NOT EXISTS schema_version (
    version INTEGER PRIMARY KEY,
    applied_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS lists (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    color TEXT NOT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS tasks (
    id TEXT PRIMARY KEY,
    title TEXT NOT NULL,
    content TEXT NOT NULL DEFAULT '',
    list_id TEXT NOT NULL,
    date TEXT NOT NULL,
    priority INTEGER NOT NULL DEFAULT 0,
    is_completed INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    completed_at TEXT NULL,
    reschedule_count INTEGER NOT NULL DEFAULT 0,
    sort_order INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS ix_tasks_date ON tasks(date);
CREATE INDEX IF NOT EXISTS ix_tasks_list ON tasks(list_id);

CREATE TABLE IF NOT EXISTS subtasks (
    id TEXT PRIMARY KEY,
    task_id TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    title TEXT NOT NULL,
    is_completed INTEGER NOT NULL DEFAULT 0,
    sort_order INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS ix_subtasks_task ON subtasks(task_id);

CREATE TABLE IF NOT EXISTS change_sets (
    id TEXT PRIMARY KEY,
    source INTEGER NOT NULL,
    label TEXT NOT NULL,
    at TEXT NOT NULL,
    undone_at TEXT NULL
);

CREATE INDEX IF NOT EXISTS ix_change_sets_at ON change_sets(at);

CREATE TABLE IF NOT EXISTS change_entries (
    change_set_id TEXT NOT NULL REFERENCES change_sets(id) ON DELETE CASCADE,
    seq INTEGER NOT NULL,
    kind INTEGER NOT NULL,
    entity_id TEXT NOT NULL,
    before_json TEXT NULL,
    after_json TEXT NULL,
    PRIMARY KEY (change_set_id, seq)
);
