using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using VGList.App.Models;
using VGList.App.Services;

namespace VGList.Agent.Clients;

/// <summary>流式工具调用早期探测通知接口：在模型开始吐出 tool_calls / tool_use 首个分片时即时通知 UI 展示「Lana 正在…」。</summary>
public interface IToolCallStreamNotifier
{
    event Action<string>? ToolCallStreamingStarted;
}

/// <summary>
/// 轻量原生 OpenAI 兼容协议 IChatClient 适配器：
/// 支持流式 SSE、多轮工具调用（tool_calls 增量聚合与早期通知）、多模态图片输入（image_url）、自定义 BaseUrl 与脱敏中文错误。
/// </summary>
public sealed class OpenAiCompatibleChatClient : IChatClient, IToolCallStreamNotifier
{
    private readonly ResolvedModel _model;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public OpenAiCompatibleChatClient(ResolvedModel model, HttpClient? httpClient = null)
    {
        _model = model;
        _ownsHttp = httpClient is null;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    }

    public event Action<string>? ToolCallStreamingStarted;

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var text = new StringBuilder();
        var contents = new List<AIContent>();

        await foreach (var update in GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            foreach (var c in update.Contents)
            {
                if (c is TextContent tc)
                {
                    text.Append(tc.Text);
                }
                else
                {
                    contents.Add(c);
                }
            }
        }

        if (text.Length > 0)
        {
            contents.Insert(0, new TextContent(text.ToString()));
        }

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, contents))
        {
            ModelId = _model.Model,
        };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var endpoint = $"{_model.BaseUrl.TrimEnd('/')}/chat/completions";
        var payload = BuildRequestJson(messages, options, stream: true);

        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (!string.IsNullOrWhiteSpace(_model.ApiKey))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _model.ApiKey);
        }

        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("连接模型服务超时，请检查网络或代理设置后重试。");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(ChatClientErrorMapper.FormatNetworkError(ex, _model.ApiKey));
        }

        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    ChatClientErrorMapper.FormatHttpError((int)resp.StatusCode, body, _model.ApiKey));
            }

            // 兼容某些网关忽略 stream=true 直接返回完整 JSON
            var mediaType = resp.Content.Headers.ContentType?.MediaType;
            if (string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            {
                var fullBody = await resp.Content.ReadAsStringAsync(cancellationToken);
                foreach (var u in ParseNonStreamingJson(fullBody))
                {
                    yield return u;
                }

                yield break;
            }

            // 聚合流式 tool_calls 分片（按 index 分组）
            var pendingCalls = new SortedDictionary<int, ToolCallAccumulator>();
            var knownToolNames = options?.Tools is { Count: > 0 }
                ? new HashSet<string>(options.Tools.OfType<AIFunction>().Select(f => f.Name), StringComparer.Ordinal)
                : null;

            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
                {
                    continue;
                }

                var data = line[5..].Trim();
                if (data == "[DONE]")
                {
                    break;
                }

                string? textDelta = null;
                try
                {
                    using var doc = JsonDocument.Parse(data);
                    if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                    {
                        continue;
                    }

                    var choice = choices[0];
                    if (choice.TryGetProperty("delta", out var delta))
                    {
                        if (delta.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
                        {
                            textDelta = contentEl.GetString();
                        }

                        if (delta.TryGetProperty("tool_calls", out var tcArr) && tcArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var tc in tcArr.EnumerateArray())
                            {
                                var idx = tc.TryGetProperty("index", out var idxEl) && idxEl.TryGetInt32(out var parsedIdx)
                                    ? parsedIdx
                                    : pendingCalls.Count;

                                if (!pendingCalls.TryGetValue(idx, out var acc))
                                {
                                    acc = new ToolCallAccumulator();
                                    pendingCalls[idx] = acc;
                                }

                                if (tc.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                                {
                                    var id = idEl.GetString();
                                    if (!string.IsNullOrEmpty(id))
                                    {
                                        acc.Id = id;
                                    }
                                }

                                if (tc.TryGetProperty("function", out var fn))
                                {
                                    if (fn.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String)
                                    {
                                        acc.Name.Append(nEl.GetString());
                                    }

                                    if (fn.TryGetProperty("arguments", out var aEl) && aEl.ValueKind == JsonValueKind.String)
                                    {
                                        acc.Arguments.Append(aEl.GetString());
                                    }

                                    // 模型一旦输出完整工具名或开始吐出参数，立即通知 UI 展示「Lana 正在…」
                                    if (!acc.NotifiedStarted && acc.Name.Length > 0)
                                    {
                                        var candidateName = acc.Name.ToString().Trim();
                                        if ((knownToolNames is not null && knownToolNames.Contains(candidateName)) ||
                                            acc.Arguments.Length > 0)
                                        {
                                            acc.NotifiedStarted = true;
                                            ToolCallStreamingStarted?.Invoke(candidateName);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // 忽略单行非标准心跳
                }

                if (!string.IsNullOrEmpty(textDelta))
                {
                    yield return new ChatResponseUpdate(ChatRole.Assistant, textDelta);
                }
            }

            // 本轮流式结束时，把聚合好的工具调用作为 FunctionCallContent 发出给 FunctionInvokingChatClient
            if (pendingCalls.Count > 0)
            {
                var callContents = new List<AIContent>();
                foreach (var (_, acc) in pendingCalls)
                {
                    var name = acc.Name.ToString().Trim();
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    var callId = string.IsNullOrEmpty(acc.Id) ? $"call_{Guid.NewGuid():N}"[..16] : acc.Id;
                    var args = ParseArgumentsDictionary(acc.Arguments.ToString());
                    callContents.Add(new FunctionCallContent(callId, name, args));
                }

                if (callContents.Count > 0)
                {
                    yield return new ChatResponseUpdate(ChatRole.Assistant, callContents);
                }
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(ChatClientMetadata)
            ? new ChatClientMetadata("OpenAiCompatible", new Uri(_model.BaseUrl), _model.Model)
            : serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    private string BuildRequestJson(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
    {
        var root = new JsonObject
        {
            ["model"] = options?.ModelId ?? _model.Model,
            ["stream"] = stream,
        };

        if (options?.Temperature is { } temp)
        {
            root["temperature"] = temp;
        }

        var msgArray = new JsonArray();
        foreach (var msg in messages)
        {
            if (msg.Role == ChatRole.Tool)
            {
                foreach (var fr in msg.Contents.OfType<FunctionResultContent>())
                {
                    msgArray.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = fr.CallId,
                        ["content"] = SerializeToolResult(fr.Result),
                    });
                }

                continue;
            }

            var role = msg.Role == ChatRole.System ? "system"
                : msg.Role == ChatRole.Assistant ? "assistant"
                : "user";

            var obj = new JsonObject { ["role"] = role };

            var images = msg.Contents.OfType<DataContent>().Where(d => d.HasTopLevelMediaType("image")).ToList();
            var toolCalls = msg.Contents.OfType<FunctionCallContent>().ToList();
            var text = string.Concat(msg.Contents.OfType<TextContent>().Select(t => t.Text));

            if (images.Count > 0 && role == "user")
            {
                var parts = new JsonArray();
                if (!string.IsNullOrEmpty(text))
                {
                    parts.Add(new JsonObject { ["type"] = "text", ["text"] = text });
                }

                foreach (var img in images)
                {
                    var b64 = Convert.ToBase64String(img.Data.ToArray());
                    var mime = string.IsNullOrWhiteSpace(img.MediaType) ? "image/jpeg" : img.MediaType;
                    parts.Add(new JsonObject
                    {
                        ["type"] = "image_url",
                        ["image_url"] = new JsonObject { ["url"] = $"data:{mime};base64,{b64}" },
                    });
                }

                obj["content"] = parts;
            }
            else
            {
                obj["content"] = string.IsNullOrEmpty(text) && toolCalls.Count > 0 ? null : text;
            }

            if (toolCalls.Count > 0 && role == "assistant")
            {
                var callsArr = new JsonArray();
                foreach (var fc in toolCalls)
                {
                    var argJson = fc.Arguments is null ? "{}" : JsonSerializer.Serialize(fc.Arguments);
                    callsArr.Add(new JsonObject
                    {
                        ["id"] = fc.CallId,
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = fc.Name,
                            ["arguments"] = argJson,
                        },
                    });
                }

                obj["tool_calls"] = callsArr;
            }

            msgArray.Add(obj);
        }

        root["messages"] = msgArray;

        if (options?.Tools is { Count: > 0 })
        {
            var toolsArr = new JsonArray();
            foreach (var fn in options.Tools.OfType<AIFunction>())
            {
                var schemaNode = JsonNode.Parse(fn.JsonSchema.GetRawText()) ?? new JsonObject { ["type"] = "object" };
                toolsArr.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = fn.Name,
                        ["description"] = fn.Description,
                        ["parameters"] = schemaNode,
                    },
                });
            }

            if (toolsArr.Count > 0)
            {
                root["tools"] = toolsArr;
            }
        }

        return root.ToJsonString();
    }

    private static IEnumerable<ChatResponseUpdate> ParseNonStreamingJson(string body)
    {
        var list = new List<ChatResponseUpdate>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            {
                return list;
            }

            var message = choices[0].GetProperty("message");
            if (message.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
            {
                var txt = contentEl.GetString();
                if (!string.IsNullOrEmpty(txt))
                {
                    list.Add(new ChatResponseUpdate(ChatRole.Assistant, txt));
                }
            }

            if (message.TryGetProperty("tool_calls", out var tcArr) && tcArr.ValueKind == JsonValueKind.Array)
            {
                var calls = new List<AIContent>();
                foreach (var tc in tcArr.EnumerateArray())
                {
                    var id = tc.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    var fn = tc.GetProperty("function");
                    var name = fn.GetProperty("name").GetString() ?? string.Empty;
                    var argText = fn.TryGetProperty("arguments", out var aEl) ? aEl.GetString() : "{}";
                    calls.Add(new FunctionCallContent(id ?? Guid.NewGuid().ToString("N")[..12], name, ParseArgumentsDictionary(argText)));
                }

                if (calls.Count > 0)
                {
                    list.Add(new ChatResponseUpdate(ChatRole.Assistant, calls));
                }
            }
        }
        catch
        {
        }

        return list;
    }

    internal static IDictionary<string, object?> ParseArgumentsDictionary(string? json)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
        {
            return dict;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    dict[prop.Name] = prop.Value.Clone();
                }
            }
        }
        catch
        {
        }

        return dict;
    }

    internal static string SerializeToolResult(object? result) => result switch
    {
        null => "null",
        string s => s,
        JsonElement je => je.GetRawText(),
        _ => JsonSerializer.Serialize(result),
    };

    private sealed class ToolCallAccumulator
    {
        public string Id { get; set; } = string.Empty;
        public StringBuilder Name { get; } = new();
        public StringBuilder Arguments { get; } = new();
        public bool NotifiedStarted { get; set; }
    }
}
