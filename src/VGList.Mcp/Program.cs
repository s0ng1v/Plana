using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VGList.Platform;

namespace VGList.Mcp;

/// <summary>
/// Plana 本地 MCP stdio 代理进程（ADR-0052）：
/// - 实现标准 MCP JSON-RPC 2.0 over stdio（兼容 Claude Desktop / Cursor / Antigravity / Codex 等）；
/// - 零业务逻辑、零仓储直连、不自动拉起 GUI；
/// - 仅将工具调用通过当前用户专属命名管道 plana-mcp-&lt;用户哈希&gt; 转发到运行中的 Plana 主进程。
/// </summary>
public static class Program
{
    private const int BridgeProtocolVersion = 1;
    private const string ServerName = "plana";
    private const string ServerVersion = "1.0.0";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static string _clientName = "外部 AI";

    public static async Task<int> Main(string[] args)
    {
        Console.InputEncoding = new UTF8Encoding(false);
        Console.OutputEncoding = new UTF8Encoding(false);

        var pipeName = ResolvePipeName(args);
        var stdin = Console.In;
        var stdout = Console.Out;

        while (true)
        {
            var line = await stdin.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string? responseLine;
            try
            {
                responseLine = await ProcessJsonRpcLineAsync(line, pipeName).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                responseLine = SerializeRpcError(null, -32603, $"Internal error: {ex.Message}");
            }

            if (responseLine is not null)
            {
                await stdout.WriteLineAsync(responseLine).ConfigureAwait(false);
                await stdout.FlushAsync().ConfigureAwait(false);
            }
        }

        return 0;
    }

    internal static async Task<string?> ProcessJsonRpcLineAsync(string line, string pipeName)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(line);
        }
        catch
        {
            return SerializeRpcError(null, -32700, "Parse error");
        }

        if (root is not JsonObject obj)
        {
            return SerializeRpcError(null, -32600, "Invalid Request");
        }

        var idNode = obj["id"]?.DeepClone();
        var method = obj["method"]?.GetValue<string>() ?? string.Empty;
        var paramsObj = obj["params"] as JsonObject;

        // JSON-RPC 通知（无 id）不返回响应
        if (idNode is null && method.StartsWith("notifications/", StringComparison.Ordinal))
        {
            return null;
        }

        switch (method)
        {
            case "initialize":
            {
                if (paramsObj?["clientInfo"] is JsonObject ci &&
                    ci["name"]?.GetValue<string>() is { Length: > 0 } name)
                {
                    _clientName = name.Trim();
                }

                var reqVersion = paramsObj?["protocolVersion"]?.GetValue<string>();
                var negotiated = reqVersion is "2024-11-05" or "2025-03-26" or "2025-06-18"
                    ? reqVersion
                    : "2024-11-05";

                return SerializeRpcResult(idNode, new
                {
                    protocolVersion = negotiated,
                    capabilities = new
                    {
                        tools = new { listChanged = false },
                    },
                    serverInfo = new
                    {
                        name = ServerName,
                        version = ServerVersion,
                    },
                    instructions = "Plana 桌面日程与任务管理工具集。所有日期请使用 yyyy-MM-dd 格式；修改/完成/删除前请先用 list_tasks 或 get_today 获取真实 taskId。",
                });
            }

            case "ping":
                return SerializeRpcResult(idNode, new { });

            case "tools/list":
                return SerializeRpcResult(idNode, new { tools = BuildToolCatalog() });

            case "tools/call":
            {
                var toolName = paramsObj?["name"]?.GetValue<string>() ?? string.Empty;
                var toolArgs = paramsObj?["arguments"];
                var callResult = await ForwardToPipeAsync(pipeName, _clientName, toolName, toolArgs).ConfigureAwait(false);
                return SerializeRpcResult(idNode, callResult);
            }

            default:
                if (idNode is null)
                {
                    return null;
                }

                return SerializeRpcError(idNode, -32601, $"Method not found: {method}");
        }
    }

    private static async Task<object> ForwardToPipeAsync(
        string pipeName,
        string clientName,
        string toolName,
        JsonNode? toolArgs)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await client.ConnectAsync(connectCts.Token).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return ToolError("Plana 未运行，或未在设置中开启「外部 AI 接入」。请先启动 Plana 并在 设置 → 助理与记忆 → 外部 AI 接入 中打开开关。");
            }
            catch (OperationCanceledException)
            {
                return ToolError("Plana 未运行，或未在设置中开启「外部 AI 接入」。请先启动 Plana 并在 设置 → 助理与记忆 → 外部 AI 接入 中打开开关。");
            }
            catch (IOException)
            {
                return ToolError("Plana 未运行，或未开启「外部 AI 接入」。");
            }

            using var writer = new StreamWriter(client, new UTF8Encoding(false), bufferSize: 8192, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);

            var payload = new JsonObject
            {
                ["v"] = BridgeProtocolVersion,
                ["id"] = Guid.NewGuid().ToString("N")[..10],
                ["clientName"] = clientName,
                ["tool"] = toolName,
                ["args"] = toolArgs?.DeepClone() ?? new JsonObject(),
            };

            await writer.WriteLineAsync(payload.ToJsonString()).ConfigureAwait(false);

            // 删除与批量改期在主窗口内最长等待 90s，这里预留 100s 读取超时
            using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(100));
            var respLine = await reader.ReadLineAsync(readCts.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(respLine))
            {
                return ToolError("与 Plana 的连接已断开（可能已在设置中关闭外部 AI 接入）。");
            }

            var respObj = JsonNode.Parse(respLine) as JsonObject;
            if (respObj is null)
            {
                return ToolError("收到无效的 Plana 响应。");
            }

            var ok = respObj["ok"]?.GetValue<bool>() == true;
            if (ok)
            {
                var resultJson = respObj["result"]?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "{}";
                return new
                {
                    content = new object[] { new { type = "text", text = resultJson } },
                    isError = false,
                };
            }

            var errCode = respObj["error"]?["code"]?.GetValue<string>() ?? "error";
            var errMsg = respObj["error"]?["message"]?.GetValue<string>() ?? "请求被拒绝";
            return ToolError($"[{errCode}] {errMsg}");
        }
        catch (OperationCanceledException)
        {
            return ToolError("等待 Plana 响应超时。");
        }
        catch (Exception ex)
        {
            return ToolError($"连接 Plana 失败：{ex.Message}");
        }
    }

    private static object ToolError(string message) => new
    {
        content = new object[] { new { type = "text", text = message } },
        isError = true,
    };

    private static string SerializeRpcResult(JsonNode? id, object result)
    {
        var envelope = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = JsonSerializer.SerializeToNode(result, JsonOpts),
        };
        return envelope.ToJsonString();
    }

    private static string SerializeRpcError(JsonNode? id, int code, string message)
    {
        var envelope = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
            },
        };
        return envelope.ToJsonString();
    }

    private static string ResolvePipeName(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--pipe", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(args[i + 1]))
            {
                return args[i + 1].Trim();
            }
        }

        return McpPipeName.ForCurrentUser();
    }

    internal static object[] BuildToolCatalog() =>
    [
        new
        {
            name = "get_today",
            description = "获取今天日期、农历/节气/节日、逾期任务数、今日任务列表与全部清单。",
            inputSchema = new { type = "object", properties = new { } },
        },
        new
        {
            name = "list_tasks",
            description = "按日期范围、清单或完成状态查询任务（单次最多返回 200 条）。",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    from = new { type = "string", description = "起始日期 yyyy-MM-dd（可选）" },
                    to = new { type = "string", description = "结束日期 yyyy-MM-dd（可选）" },
                    listId = new { type = "string", description = "清单 Id（可选，inbox 为收集箱）" },
                    includeCompleted = new { type = "boolean", description = "是否包含已完成任务，默认 false" },
                    limit = new { type = "integer", description = "返回条数上限（1–200，默认 100）" },
                },
            },
        },
        new
        {
            name = "list_lists",
            description = "列出 Plana 中所有任务清单及其未完成数量。",
            inputSchema = new { type = "object", properties = new { } },
        },
        new
        {
            name = "resolve_date",
            description = "离线解析中文自然语言日期（如「下周三交周报」「月底前续费」「中秋节」）。",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    text = new { type = "string", description = "包含中文日期的短语或任务文本" },
                    baseDate = new { type = "string", description = "基准日期 yyyy-MM-dd（可选，默认今天）" },
                },
                required = new[] { "text" },
            },
        },
        new
        {
            name = "cn_calendar_info",
            description = "查询指定日期的农历、二十四节气、公农历节日与星期信息（1901–2100 离线计算）。",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    date = new { type = "string", description = "公历日期 yyyy-MM-dd（可选，默认今天）" },
                },
            },
        },
        new
        {
            name = "create_tasks",
            description = "批量新建任务（单次最多 20 条，立即写入并支持撤销）。",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    tasks = new
                    {
                        type = "array",
                        description = "要创建的任务数组（1–20 条）",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                title = new { type = "string", description = "任务标题（必填，≤200 字）" },
                                date = new { type = "string", description = "任务日期 yyyy-MM-dd（默认今天）" },
                                priority = new { type = "string", description = "None / Low / Medium / High" },
                                listId = new { type = "string", description = "目标清单 Id（默认 inbox）" },
                                content = new { type = "string", description = "任务详情（≤8000 字）" },
                                subtasks = new { type = "array", items = new { type = "string" }, description = "子待办标题列表" },
                            },
                            required = new[] { "title" },
                        },
                    },
                },
                required = new[] { "tasks" },
            },
        },
        new
        {
            name = "update_task",
            description = "修改单条已有任务（标题、日期、优先级、清单、详情或追加子待办，支持撤销）。",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    taskId = new { type = "string", description = "真实任务 Id（由 list_tasks / get_today 获取）" },
                    title = new { type = "string", description = "新标题（可选）" },
                    date = new { type = "string", description = "新日期 yyyy-MM-dd（可选）" },
                    priority = new { type = "string", description = "None / Low / Medium / High（可选）" },
                    listId = new { type = "string", description = "新清单 Id（可选）" },
                    content = new { type = "string", description = "新详情描述（可选）" },
                    appendSubtasks = new { type = "array", items = new { type = "string" }, description = "追加子待办（可选）" },
                },
                required = new[] { "taskId" },
            },
        },
        new
        {
            name = "complete_task",
            description = "标记任务已完成或恢复未完成。",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    taskId = new { type = "string", description = "真实任务 Id" },
                    completed = new { type = "boolean", description = "true=已完成，false=未完成（默认 true）" },
                },
                required = new[] { "taskId" },
            },
        },
        new
        {
            name = "reschedule_tasks",
            description = "批量改期任务（1–5 项直接执行；超过 5 项将在 Plana 主窗口内等待用户确认，90 秒超时自动拒绝）。",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    items = new
                    {
                        type = "array",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                taskId = new { type = "string", description = "真实任务 Id" },
                                newDate = new { type = "string", description = "目标日期 yyyy-MM-dd" },
                            },
                            required = new[] { "taskId", "newDate" },
                        },
                    },
                },
                required = new[] { "items" },
            },
        },
        new
        {
            name = "delete_tasks",
            description = "删除指定任务（高风险：始终在 Plana 主窗口内等待用户确认，90 秒超时自动拒绝；确认删除后仍可撤销）。",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    taskIds = new
                    {
                        type = "array",
                        items = new { type = "string" },
                        description = "要删除的任务 Id 列表（1–50 条）",
                    },
                },
                required = new[] { "taskIds" },
            },
        },
    ];
}
