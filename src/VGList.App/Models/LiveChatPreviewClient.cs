using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace VGList.App.Models;

/// <summary>
/// 极轻量的纯 HttpClient SSE 流式对话客户端（零 AI SDK 依赖）：
/// 用于 A0–A2 阶段对话页面在用户已配置真实模型时提供丝滑的实时流式体验；A3 接入完整 Agent 内核后由 VGList.Agent 接管。
/// </summary>
public sealed class LiveChatPreviewClient
{
    private readonly HttpClient _http;

    public LiveChatPreviewClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            UseProxy = true,
        })
        {
            Timeout = TimeSpan.FromSeconds(90),
        };
    }

    public async IAsyncEnumerable<string> StreamReplyAsync(
        ResolvedModel model,
        string systemPrompt,
        IReadOnlyList<(string Role, string Content)> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (model.Protocol == ModelProtocol.Anthropic)
        {
            await foreach (var delta in StreamAnthropicAsync(model, systemPrompt, history, ct))
            {
                yield return delta;
            }

            yield break;
        }

        await foreach (var delta in StreamOpenAiAsync(model, systemPrompt, history, ct))
        {
            yield return delta;
        }
    }

    private async IAsyncEnumerable<string> StreamOpenAiAsync(
        ResolvedModel model,
        string systemPrompt,
        IReadOnlyList<(string Role, string Content)> history,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var messages = new List<object> { new { role = "system", content = systemPrompt } };
        foreach (var (role, content) in history)
        {
            messages.Add(new { role, content });
        }

        var payload = JsonSerializer.Serialize(new
        {
            model = model.Model,
            messages,
            stream = true,
        });

        var endpoint = ModelConnectionTester.Combine(model.BaseUrl, "chat/completions");
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        ModelConnectionTester.ApplyAuthHeaders(req, ModelProtocol.OpenAi, model.ApiKey);

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var errBody = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"模型响应失败 (HTTP {(int)resp.StatusCode})：{ModelConnectionTester.Sanitize(errBody, model.ApiKey)}");
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                break;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[5..].Trim();
            if (data == "[DONE]" || string.IsNullOrEmpty(data))
            {
                continue;
            }

            if (TryExtractOpenAiDelta(data, out var text) && !string.IsNullOrEmpty(text))
            {
                yield return text;
            }
        }
    }

    private async IAsyncEnumerable<string> StreamAnthropicAsync(
        ResolvedModel model,
        string systemPrompt,
        IReadOnlyList<(string Role, string Content)> history,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var messages = history.Select(h => new { role = h.Role, content = h.Content }).ToList();
        var payload = JsonSerializer.Serialize(new
        {
            model = model.Model,
            max_tokens = 2048,
            system = systemPrompt,
            messages,
            stream = true,
        });

        var endpoint = ModelConnectionTester.Combine(model.BaseUrl, "v1/messages");
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        ModelConnectionTester.ApplyAuthHeaders(req, ModelProtocol.Anthropic, model.ApiKey);

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var errBody = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"模型响应失败 (HTTP {(int)resp.StatusCode})：{ModelConnectionTester.Sanitize(errBody, model.ApiKey)}");
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                break;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[5..].Trim();
            if (string.IsNullOrEmpty(data))
            {
                continue;
            }

            if (TryExtractAnthropicDelta(data, out var text) && !string.IsNullOrEmpty(text))
            {
                yield return text;
            }
        }
    }

    private static bool TryExtractOpenAiDelta(string json, out string text)
    {
        text = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                if (first.TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("content", out var content) &&
                    content.ValueKind == JsonValueKind.String)
                {
                    text = content.GetString() ?? string.Empty;
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool TryExtractAnthropicDelta(string json, out string text)
    {
        text = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("type", out var t) &&
                string.Equals(t.GetString(), "content_block_delta", StringComparison.Ordinal) &&
                doc.RootElement.TryGetProperty("delta", out var delta) &&
                delta.TryGetProperty("text", out var content) &&
                content.ValueKind == JsonValueKind.String)
            {
                text = content.GetString() ?? string.Empty;
                return true;
            }
        }
        catch
        {
        }

        return false;
    }
}
