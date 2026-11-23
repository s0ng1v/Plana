using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using VGList.App.Models;
using VGList.App.Services;

namespace VGList.Agent.Clients;

/// <summary>
/// 轻量原生 Anthropic 兼容协议 IChatClient 适配器：
/// 支持 Anthropic 官方及 DeepSeek/Kimi/MiniMax/火山引擎的 Anthropic 兼容端点，
/// 支持自定义 BaseUrl、流式文本与 tool_use 聚合、多模态图片输入与脱敏中文错误。
/// </summary>
public sealed class AnthropicCompatibleChatClient : IChatClient, IToolCallStreamNotifier
{
    private readonly ResolvedModel _model;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public AnthropicCompatibleChatClient(ResolvedModel model, HttpClient? httpClient = null)
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
        var trimmed = _model.BaseUrl.TrimEnd('/');
        var endpoint = trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? $"{trimmed}/messages"
            : $"{trimmed}/v1/messages";

        var payload = BuildRequestJson(messages, options);
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (!string.IsNullOrWhiteSpace(_model.ApiKey))
        {
            req.Headers.TryAddWithoutValidation("x-api-key", _model.ApiKey);
        }

        req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
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

            var toolBlocks = new SortedDictionary<int, (string Id, string Name, StringBuilder Json)>();

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
                    var root = doc.RootElement;
                    var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;

                    if (type == "content_block_start" &&
                        root.TryGetProperty("content_block", out var block) &&
                        block.TryGetProperty("type", out var bt) &&
                        bt.GetString() == "tool_use")
                    {
                        var idx = root.TryGetProperty("index", out var iEl) ? iEl.GetInt32() : toolBlocks.Count;
                        var id = block.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? Guid.NewGuid().ToString("N")[..12] : Guid.NewGuid().ToString("N")[..12];
                        var name = block.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? string.Empty : string.Empty;
                        toolBlocks[idx] = (id, name, new StringBuilder());
                        if (!string.IsNullOrEmpty(name))
                        {
                            ToolCallStreamingStarted?.Invoke(name);
                        }
                    }
                    else if (type == "content_block_delta" && root.TryGetProperty("delta", out var delta))
                    {
                        var dt = delta.TryGetProperty("type", out var dtEl) ? dtEl.GetString() : null;
                        if (dt == "text_delta" && delta.TryGetProperty("text", out var txtEl))
                        {
                            textDelta = txtEl.GetString();
                        }
                        else if (dt == "input_json_delta" &&
                                 root.TryGetProperty("index", out var idxEl) &&
                                 toolBlocks.TryGetValue(idxEl.GetInt32(), out var tb) &&
                                 delta.TryGetProperty("partial_json", out var pjEl))
                        {
                            tb.Json.Append(pjEl.GetString());
                        }
                    }
                }
                catch
                {
                }

                if (!string.IsNullOrEmpty(textDelta))
                {
                    yield return new ChatResponseUpdate(ChatRole.Assistant, textDelta);
                }
            }

            if (toolBlocks.Count > 0)
            {
                var calls = new List<AIContent>();
                foreach (var (_, (id, name, jsonSb)) in toolBlocks)
                {
                    if (!string.IsNullOrEmpty(name))
                    {
                        calls.Add(new FunctionCallContent(id, name, OpenAiCompatibleChatClient.ParseArgumentsDictionary(jsonSb.ToString())));
                    }
                }

                if (calls.Count > 0)
                {
                    yield return new ChatResponseUpdate(ChatRole.Assistant, calls);
                }
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(ChatClientMetadata)
            ? new ChatClientMetadata("AnthropicCompatible", new Uri(_model.BaseUrl), _model.Model)
            : serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    private string BuildRequestJson(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var systemParts = new List<string>();
        var msgArray = new JsonArray();

        foreach (var msg in messages)
        {
            if (msg.Role == ChatRole.System)
            {
                var t = string.Concat(msg.Contents.OfType<TextContent>().Select(x => x.Text));
                if (!string.IsNullOrWhiteSpace(t))
                {
                    systemParts.Add(t);
                }

                continue;
            }

            if (msg.Role == ChatRole.Tool)
            {
                var blocks = new JsonArray();
                foreach (var fr in msg.Contents.OfType<FunctionResultContent>())
                {
                    blocks.Add(new JsonObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = fr.CallId,
                        ["content"] = OpenAiCompatibleChatClient.SerializeToolResult(fr.Result),
                    });
                }

                msgArray.Add(new JsonObject { ["role"] = "user", ["content"] = blocks });
                continue;
            }

            var role = msg.Role == ChatRole.Assistant ? "assistant" : "user";
            var contentBlocks = new JsonArray();

            foreach (var c in msg.Contents)
            {
                switch (c)
                {
                    case TextContent tc when !string.IsNullOrEmpty(tc.Text):
                        contentBlocks.Add(new JsonObject { ["type"] = "text", ["text"] = tc.Text });
                        break;
                    case DataContent dc when dc.HasTopLevelMediaType("image"):
                        contentBlocks.Add(new JsonObject
                        {
                            ["type"] = "image",
                            ["source"] = new JsonObject
                            {
                                ["type"] = "base64",
                                ["media_type"] = string.IsNullOrWhiteSpace(dc.MediaType) ? "image/jpeg" : dc.MediaType,
                                ["data"] = Convert.ToBase64String(dc.Data.ToArray()),
                            },
                        });
                        break;
                    case FunctionCallContent fc:
                        var inputNode = fc.Arguments is null
                            ? new JsonObject()
                            : JsonNode.Parse(JsonSerializer.Serialize(fc.Arguments)) ?? new JsonObject();
                        contentBlocks.Add(new JsonObject
                        {
                            ["type"] = "tool_use",
                            ["id"] = fc.CallId,
                            ["name"] = fc.Name,
                            ["input"] = inputNode,
                        });
                        break;
                }
            }

            if (contentBlocks.Count > 0)
            {
                msgArray.Add(new JsonObject { ["role"] = role, ["content"] = contentBlocks });
            }
        }

        var root = new JsonObject
        {
            ["model"] = options?.ModelId ?? _model.Model,
            ["max_tokens"] = options?.MaxOutputTokens ?? 4096,
            ["stream"] = true,
            ["messages"] = msgArray,
        };

        if (systemParts.Count > 0)
        {
            root["system"] = string.Join("\n\n", systemParts);
        }

        if (options?.Tools is { Count: > 0 })
        {
            var toolsArr = new JsonArray();
            foreach (var fn in options.Tools.OfType<AIFunction>())
            {
                var schemaNode = JsonNode.Parse(fn.JsonSchema.GetRawText()) ?? new JsonObject { ["type"] = "object" };
                toolsArr.Add(new JsonObject
                {
                    ["name"] = fn.Name,
                    ["description"] = fn.Description,
                    ["input_schema"] = schemaNode,
                });
            }

            if (toolsArr.Count > 0)
            {
                root["tools"] = toolsArr;
            }
        }

        return root.ToJsonString();
    }
}

/// <summary>模型调用脱敏与 5 类可行动中文错误分类器。</summary>
internal static class ChatClientErrorMapper
{
    public static string FormatHttpError(int statusCode, string rawBody, string? apiKey)
    {
        var safeBody = ModelConnectionTester.Sanitize(rawBody, apiKey);
        return statusCode switch
        {
            401 or 403 => "API Key 无效或权限不足，请前往「设置 → 模型服务」检查密钥。",
            404 => "模型名称或接口地址不存在（404），请确认模型名称拼写正确。",
            429 => "请求过于频繁或账号额度不足（429），请稍后再试或检查厂商余额。",
            >= 500 => $"模型服务端暂时繁忙（HTTP {statusCode}），请稍后重试。",
            _ => $"模型请求失败（HTTP {statusCode}）：{TrimSnippet(safeBody)}",
        };
    }

    public static string FormatNetworkError(HttpRequestException ex, string? apiKey)
    {
        var msg = ModelConnectionTester.Sanitize(ex.Message, apiKey);
        if (msg.Contains("SSL", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("certificate", StringComparison.OrdinalIgnoreCase))
        {
            return "TLS/SSL 证书校验失败，请检查系统时间或网络代理配置。";
        }

        return $"无法连接到模型服务：{TrimSnippet(msg)}";
    }

    private static string TrimSnippet(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "请检查网络或配置";
        }

        var singleLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length > 140 ? singleLine[..140] + "…" : singleLine;
    }
}

/// <summary>ResolvedModel → IChatClient 工厂（可由单元测试注入脚本化假客户端）。</summary>
public interface IChatClientFactory
{
    IChatClient Create(ResolvedModel model);
}

public sealed class DefaultChatClientFactory : IChatClientFactory
{
    public IChatClient Create(ResolvedModel model) => model.Protocol == ModelProtocol.Anthropic
        ? new AnthropicCompatibleChatClient(model)
        : new OpenAiCompatibleChatClient(model);
}
