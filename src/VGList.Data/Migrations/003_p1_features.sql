-- P1：标签、重复规则与回收站软删除支持（ADR-0053）
ALTER TABLE tasks ADD COLUMN tags_json TEXT NOT NULL DEFAULT '[]';
ALTER TABLE tasks ADD COLUMN recurrence INTEGER NOT NULL DEFAULT 0;
ALTER TABLE tasks ADD COLUMN deleted_at TEXT NULL;

CREATE INDEX IF NOT EXISTS ix_tasks_deleted_at ON tasks(deleted_at);
