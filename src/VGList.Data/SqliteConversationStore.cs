using System.Text.Json;
using Microsoft.Data.Sqlite;
using VGList.Core.Abstractions;
using VGList.Core.Models;

namespace VGList.Data;

/// <summary>
/// 基于共享 WAL SQLite 的助理会话与提案记录存储（002_assistant.sql）：
/// 自动保留最近 30 个会话，附件仅存名称/大小/类型元信息，不保存原始文件内容。
/// </summary>
public sealed class SqliteConversationStore : IConversationStore
{
    public const int MaxRetainedSessions = 30;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly SqliteDatabase _db;

    public SqliteConversationStore(SqliteDatabase db)
    {
        _db = db;
    }

    public IReadOnlyList<SessionSummary> ListSessions(int limit = MaxRetainedSessions)
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "SELECT id, title, updated_at FROM assistant_sessions ORDER BY updated_at DESC LIMIT $lim;";
            cmd.Parameters.AddWithValue("$lim", Math.Max(1, limit));

            var list = new List<SessionSummary>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new SessionSummary(
                    r.GetString(0),
                    r.GetString(1),
                    DateTimeOffset.Parse(r.GetString(2))));
            }

            return list;
        }
    }

    public SessionSummary CreateSession(string? id = null, string title = "新对话")
    {
        var sid = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
        var cleanTitle = TrimTitle(title);
        var now = DateTimeOffset.Now;

        lock (_db.Gate)
        {
            if (!_db.IsReadOnlyFutureVersion)
            {
                using var tx = _db.Connection.BeginTransaction();
                using (var cmd = _db.Connection.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "INSERT OR REPLACE INTO assistant_sessions(id, title, updated_at) VALUES($id, $t, $u);";
                    cmd.Parameters.AddWithValue("$id", sid);
                    cmd.Parameters.AddWithValue("$t", cleanTitle);
                    cmd.Parameters.AddWithValue("$u", now.ToString("O"));
                    cmd.ExecuteNonQuery();
                }

                PruneOldSessionsLocked(tx);
                tx.Commit();
            }
        }

        return new SessionSummary(sid, cleanTitle, now);
    }

    public void UpdateSessionTitle(string sessionId, string title)
    {
        if (_db.IsReadOnlyFutureVersion)
        {
            return;
        }

        lock (_db.Gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO assistant_sessions(id, title, updated_at) VALUES($id, $t, $u)
                ON CONFLICT(id) DO UPDATE SET title = excluded.title, updated_at = excluded.updated_at;
                """;
            cmd.Parameters.AddWithValue("$id", sessionId);
            cmd.Parameters.AddWithValue("$t", TrimTitle(title));
            cmd.Parameters.AddWithValue("$u", DateTimeOffset.Now.ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    public void DeleteSession(string sessionId)
    {
        if (_db.IsReadOnlyFutureVersion)
        {
            return;
        }

        lock (_db.Gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM assistant_sessions WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", sessionId);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<ConversationMessageRecord> LoadMessages(string sessionId)
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT id, session_id, seq, role, content_json, created_at
                FROM assistant_messages
                WHERE session_id = $sid
                ORDER BY seq ASC;
                """;
            cmd.Parameters.AddWithValue("$sid", sessionId);

            var list = new List<ConversationMessageRecord>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var payload = JsonSerializer.Deserialize<MessagePayload>(r.GetString(4), JsonOpts) ?? new MessagePayload();
                var atts = payload.Attachments?.Select(a => new AttachmentRef(a.Name, string.Empty, a.SizeBytes, a.KindTag)).ToList();
                list.Add(new ConversationMessageRecord(
                    r.GetString(0),
                    r.GetString(1),
                    r.GetInt32(2),
                    r.GetString(3),
                    payload.Text ?? string.Empty,
                    payload.ModelTag,
                    atts,
                    DateTimeOffset.Parse(r.GetString(5))));
            }

            return list;
        }
    }

    public void AppendMessage(ConversationMessageRecord message)
    {
        if (_db.IsReadOnlyFutureVersion)
        {
            return;
        }

        // 只落附件元数据（名称、大小、类型），严禁落盘附件路径或正文内容
        var payload = new MessagePayload
        {
            Text = message.Text,
            ModelTag = message.ModelTag,
            Attachments = message.Attachments?.Select(a => new AttachmentMetaDto(a.Name, a.SizeBytes, a.KindTag)).ToList(),
        };
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        var nowStr = message.CreatedAt.ToString("O");

        lock (_db.Gate)
        {
            using var tx = _db.Connection.BeginTransaction();
            EnsureSessionExistsLocked(tx, message.SessionId, nowStr);

            using (var cmd = _db.Connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT OR REPLACE INTO assistant_messages(id, session_id, seq, role, content_json, created_at)
                    VALUES($id, $sid, $seq, $role, $json, $at);
                    """;
                cmd.Parameters.AddWithValue("$id", message.Id);
                cmd.Parameters.AddWithValue("$sid", message.SessionId);
                cmd.Parameters.AddWithValue("$seq", message.Seq);
                cmd.Parameters.AddWithValue("$role", message.Role);
                cmd.Parameters.AddWithValue("$json", json);
                cmd.Parameters.AddWithValue("$at", nowStr);
                cmd.ExecuteNonQuery();
            }

            using (var touch = _db.Connection.CreateCommand())
            {
                touch.Transaction = tx;
                touch.CommandText = "UPDATE assistant_sessions SET updated_at = $u WHERE id = $sid;";
                touch.Parameters.AddWithValue("$sid", message.SessionId);
                touch.Parameters.AddWithValue("$u", nowStr);
                touch.ExecuteNonQuery();
            }

            PruneOldSessionsLocked(tx);
            tx.Commit();
        }
    }

    public void TruncateMessages(string sessionId, int keepCount)
    {
        if (_db.IsReadOnlyFutureVersion)
        {
            return;
        }

        lock (_db.Gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            if (keepCount <= 0)
            {
                cmd.CommandText = "DELETE FROM assistant_messages WHERE session_id = $sid;";
                cmd.Parameters.AddWithValue("$sid", sessionId);
            }
            else
            {
                cmd.CommandText = """
                    DELETE FROM assistant_messages
                    WHERE session_id = $sid
                      AND id NOT IN (
                          SELECT id FROM assistant_messages
                          WHERE session_id = $sid
                          ORDER BY seq ASC
                          LIMIT $keep
                      );
                    """;
                cmd.Parameters.AddWithValue("$sid", sessionId);
                cmd.Parameters.AddWithValue("$keep", keepCount);
            }

            cmd.ExecuteNonQuery();
        }
    }

    public void SaveProposal(ProposalRecord record)
    {
        if (_db.IsReadOnlyFutureVersion)
        {
            return;
        }

        lock (_db.Gate)
        {
            using var tx = _db.Connection.BeginTransaction();
            EnsureSessionExistsLocked(tx, record.SessionId, record.CreatedAt.ToString("O"));

            using var cmd = _db.Connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT OR REPLACE INTO assistant_proposals(id, session_id, json, status, applied_changeset_id, created_at)
                VALUES($id, $sid, $json, $st, $cs, $at);
                """;
            cmd.Parameters.AddWithValue("$id", record.Id);
            cmd.Parameters.AddWithValue("$sid", record.SessionId);
            cmd.Parameters.AddWithValue("$json", record.Json);
            cmd.Parameters.AddWithValue("$st", record.Status.ToString());
            cmd.Parameters.AddWithValue("$cs", (object?)record.AppliedChangeSetId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$at", record.CreatedAt.ToString("O"));
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
    }

    public ProposalRecord? GetProposal(string proposalId)
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT id, session_id, json, status, applied_changeset_id, created_at
                FROM assistant_proposals
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", proposalId);
            using var r = cmd.ExecuteReader();
            return r.Read() ? ReadProposal(r) : null;
        }
    }

    public IReadOnlyList<ProposalRecord> LoadSessionProposals(string sessionId)
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT id, session_id, json, status, applied_changeset_id, created_at
                FROM assistant_proposals
                WHERE session_id = $sid
                ORDER BY created_at DESC;
                """;
            cmd.Parameters.AddWithValue("$sid", sessionId);

            var list = new List<ProposalRecord>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(ReadProposal(r));
            }

            return list;
        }
    }

    public void UpdateProposalStatus(string proposalId, ProposalStatus status, string? appliedChangeSetId)
    {
        if (_db.IsReadOnlyFutureVersion)
        {
            return;
        }

        lock (_db.Gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                UPDATE assistant_proposals
                SET status = $st, applied_changeset_id = COALESCE($cs, applied_changeset_id)
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", proposalId);
            cmd.Parameters.AddWithValue("$st", status.ToString());
            cmd.Parameters.AddWithValue("$cs", (object?)appliedChangeSetId ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private void EnsureSessionExistsLocked(SqliteTransaction tx, string sessionId, string nowStr)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR IGNORE INTO assistant_sessions(id, title, updated_at) VALUES($id, '新对话', $u);";
        cmd.Parameters.AddWithValue("$id", sessionId);
        cmd.Parameters.AddWithValue("$u", nowStr);
        cmd.ExecuteNonQuery();
    }

    private void PruneOldSessionsLocked(SqliteTransaction tx)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            DELETE FROM assistant_sessions
            WHERE id NOT IN (
                SELECT id FROM assistant_sessions ORDER BY updated_at DESC LIMIT $max
            );
            """;
        cmd.Parameters.AddWithValue("$max", MaxRetainedSessions);
        cmd.ExecuteNonQuery();
    }

    private static ProposalRecord ReadProposal(SqliteDataReader r)
    {
        var status = Enum.TryParse<ProposalStatus>(r.GetString(3), out var parsed)
            ? parsed
            : ProposalStatus.Pending;
        return new ProposalRecord(
            r.GetString(0),
            r.GetString(1),
            r.GetString(2),
            status,
            r.IsDBNull(4) ? null : r.GetString(4),
            DateTimeOffset.Parse(r.GetString(5)));
    }

    private static string TrimTitle(string title)
    {
        var t = (title ?? string.Empty).Trim();
        if (t.Length == 0)
        {
            return "新对话";
        }

        return t.Length > 20 ? t[..20] + "…" : t;
    }

    private sealed class MessagePayload
    {
        public string? Text { get; set; }
        public string? ModelTag { get; set; }
        public List<AttachmentMetaDto>? Attachments { get; set; }
    }

    private sealed record AttachmentMetaDto(string Name, long SizeBytes, string KindTag);
}
