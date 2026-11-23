-- A3 助理会话、消息与提案历史表（保留最近 30 个会话，附件只存元信息不落原文）

CREATE TABLE IF NOT EXISTS assistant_sessions (
    id TEXT PRIMARY KEY,
    title TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_assistant_sessions_updated ON assistant_sessions(updated_at DESC);

CREATE TABLE IF NOT EXISTS assistant_messages (
    id TEXT PRIMARY KEY,
    session_id TEXT NOT NULL,
    seq INTEGER NOT NULL,
    role TEXT NOT NULL,
    content_json TEXT NOT NULL,
    created_at TEXT NOT NULL,
    FOREIGN KEY (session_id) REFERENCES assistant_sessions(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_assistant_messages_session ON assistant_messages(session_id, seq);

CREATE TABLE IF NOT EXISTS assistant_proposals (
    id TEXT PRIMARY KEY,
    session_id TEXT NOT NULL,
    json TEXT NOT NULL,
    status TEXT NOT NULL,
    applied_changeset_id TEXT NULL,
    created_at TEXT NOT NULL,
    FOREIGN KEY (session_id) REFERENCES assistant_sessions(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_assistant_proposals_session ON assistant_proposals(session_id, created_at DESC);
