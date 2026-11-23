-- Migration 004: 日程具体时刻与提醒配置（ADR-0055）
-- due_time: "HH:mm" 可空；reminder_json: TaskReminder JSON 序列化，可空。

ALTER TABLE tasks ADD COLUMN due_time TEXT NULL;
ALTER TABLE tasks ADD COLUMN reminder_json TEXT NULL;
