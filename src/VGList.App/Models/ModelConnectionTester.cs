using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VGList.App.Models;

/// <summary>
/// 纯 HttpClient 实现的连通性测试与模型列表拉取器（不依赖任何 AI SDK，保持常驻档轻量）。
/// 支持 OpenAI 兼容与 Anthropic 兼容两套协议，自动脱敏错误文本中的 Key 形态串。
/// </summary>
public sealed class ModelConnectionTester : IModelConnectionTester
{
    private static readonly Regex SecretPattern = new(
        @"(Bearer\s+[A-Za-z0-9._\-]{8,}|sk-[A-Za-z0-9_\-]{8,}|ak-[A-Za-z0-9_\-]{8,})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly HttpClient _http;
    private readonly TimeSpan _timeout;

    public ModelConnectionTester(HttpClient? httpClient = null, TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(15);
        _http = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            UseProxy = true,
        });
    }

    public async Task<ConnectionTestResult> TestAsync(
        string baseUrl,
        ModelProtocol protocol,
        string apiKey,
        string model,
        bool needsKey,
        CancellationToken ct = default)
    {
        if (!ProviderCatalog.IsValidBaseUrl(baseUrl, out var urlErr))
        {
            return new ConnectionTestResult(false, urlErr!);
        }

        if (needsKey && string.IsNullOrWhiteSpace(apiKey))
        {
            return new ConnectionTestResult(false, "请填写 API Key");
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            return new ConnectionTestResult(false, "请填写模型名称");
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(_timeout);

        var sw = Stopwatch.StartNew();
        try
        {
            if (protocol == ModelProtocol.Anthropic)
            {
                return await TestAnthropicAsync(baseUrl, apiKey, model.Trim(), sw, linked.Token);
            }

            return await TestOpenAiAsync(baseUrl, apiKey, model.Trim(), sw, linked.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ConnectionTestResult(false, "连接超时（15 秒未响应），请检查网络或代理设置", LatencyMs: (int)sw.ElapsedMilliseconds);
        }
        catch (HttpRequestException ex) when (HasCertificateError(ex))
        {
            return new ConnectionTestResult(false, "HTTPS 证书验证失败，请检查地址或系统代理证书", LatencyMs: (int)sw.ElapsedMilliseconds);
        }
        catch (HttpRequestException)
        {
            return new ConnectionTestResult(false, "网络不通或无法解析该域名，请检查网络连接与地址", LatencyMs: (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, $"请求失败：{Sanitize(ex.Message, apiKey)}", LatencyMs: (int)sw.ElapsedMilliseconds);
        }
    }

    public async Task<ModelListResult> FetchModelsAsync(
        string baseUrl,
        ModelProtocol protocol,
        string apiKey,
        CancellationToken ct = default)
    {
        if (!ProviderCatalog.IsValidBaseUrl(baseUrl, out var urlErr))
        {
            return new ModelListResult(false, [], urlErr);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(_timeout);

        try
        {
            var endpoint = protocol == ModelProtocol.Anthropic
                ? Combine(baseUrl, "v1/models")
                : Combine(baseUrl, "models");

            using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
            ApplyAuthHeaders(req, protocol, apiKey);

            using var resp = await _http.SendAsync(req, linked.Token);
            if (!resp.IsSuccessStatusCode)
            {
                return new ModelListResult(false, [], "该服务不支持自动拉取模型列表，请手动填写模型名");
            }

            var body = await resp.Content.ReadAsStringAsync(linked.Token);
            using var doc = JsonDocument.Parse(body);
            var ids = new List<string>();
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                    {
                        var id = idEl.GetString();
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            ids.Add(id);
                        }
                    }
                }
            }

            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids.Count > 0
                ? new ModelListResult(true, ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList())
                : new ModelListResult(false, [], "返回的模型列表为空，请手动填写模型名");
        }
        catch
        {
            return new ModelListResult(false, [], "无法拉取模型列表，请手动填写模型名");
        }
    }

    private async Task<ConnectionTestResult> TestOpenAiAsync(
        string baseUrl,
        string apiKey,
        string model,
        Stopwatch sw,
        CancellationToken ct)
    {
        var endpoint = Combine(baseUrl, "chat/completions");
        var (status, body) = await SendOpenAiPingAsync(endpoint, apiKey, model, useMaxCompletionTokens: false, ct);

        // 某些新推理模型要求 max_completion_tokens 而非 max_tokens，遇到 400 自动切换重试一次
        if (status == HttpStatusCode.BadRequest &&
            body.Contains("max_completion_tokens", StringComparison.OrdinalIgnoreCase))
        {
            (status, body) = await SendOpenAiPingAsync(endpoint, apiKey, model, useMaxCompletionTokens: true, ct);
        }

        return InterpretResponse(status, body, apiKey, (int)sw.ElapsedMilliseconds);
    }

    private async Task<(HttpStatusCode Status, string Body)> SendOpenAiPingAsync(
        string endpoint,
        string apiKey,
        string model,
        bool useMaxCompletionTokens,
        CancellationToken ct)
    {
        var payload = useMaxCompletionTokens
            ? JsonSerializer.Serialize(new
            {
                model,
                messages = new[] { new { role = "user", content = "ping" } },
                max_completion_tokens = 1,
            })
            : JsonSerializer.Serialize(new
            {
                model,
                messages = new[] { new { role = "user", content = "ping" } },
                max_tokens = 1,
            });

        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        ApplyAuthHeaders(req, ModelProtocol.OpenAi, apiKey);

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        return (resp.StatusCode, body);
    }

    private async Task<ConnectionTestResult> TestAnthropicAsync(
        string baseUrl,
        string apiKey,
        string model,
        Stopwatch sw,
        CancellationToken ct)
    {
        var endpoint = Combine(baseUrl, "v1/messages");
        var payload = JsonSerializer.Serialize(new
        {
            model,
            max_tokens = 1,
            messages = new[] { new { role = "user", content = "ping" } },
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        ApplyAuthHeaders(req, ModelProtocol.Anthropic, apiKey);

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        return InterpretResponse(resp.StatusCode, body, apiKey, (int)sw.ElapsedMilliseconds);
    }

    private static ConnectionTestResult InterpretResponse(HttpStatusCode status, string body, string apiKey, int latencyMs)
    {
        var code = (int)status;
        if (code is >= 200 and < 300)
        {
            return new ConnectionTestResult(true, $"连接成功（{latencyMs} ms）", code, latencyMs);
        }

        var detail = ExtractServerErrorMessage(body, apiKey);
        var suffix = string.IsNullOrEmpty(detail) ? string.Empty : $"（{detail}）";

        var message = status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                $"API Key 无效或没有权限，请检查 Key 是否正确{suffix}",
            HttpStatusCode.NotFound =>
                $"接口地址或模型不存在，请检查 Base URL 与模型名{suffix}",
            HttpStatusCode.TooManyRequests =>
                $"触发限流或额度不足，请检查账户余额与配额{suffix}",
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout =>
                $"服务端响应超时，请稍后重试{suffix}",
            >= HttpStatusCode.InternalServerError =>
                $"模型服务端暂时故障（HTTP {code}），请稍后重试{suffix}",
            _ =>
                $"请求被拒绝（HTTP {code}），请检查模型名与配置{suffix}",
        };

        return new ConnectionTestResult(false, message, code, latencyMs);
    }

    internal static void ApplyAuthHeaders(HttpRequestMessage req, ModelProtocol protocol, string apiKey)
    {
        if (protocol == ModelProtocol.Anthropic)
        {
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                req.Headers.TryAddWithoutValidation("x-api-key", apiKey.Trim());
            }

            req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
            }
        }
    }

    internal static string Combine(string baseUrl, string relative)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');
        if (relative.StartsWith("v1/", StringComparison.OrdinalIgnoreCase) &&
            trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            relative = relative[3..];
        }

        return $"{trimmed}/{relative.TrimStart('/')}";
    }

    private static string ExtractServerErrorMessage(string body, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.Object &&
                    err.TryGetProperty("message", out var msg) &&
                    msg.ValueKind == JsonValueKind.String)
                {
                    return Sanitize(msg.GetString() ?? string.Empty, apiKey);
                }

                if (err.ValueKind == JsonValueKind.String)
                {
                    return Sanitize(err.GetString() ?? string.Empty, apiKey);
                }
            }

            if (doc.RootElement.TryGetProperty("message", out var topMsg) && topMsg.ValueKind == JsonValueKind.String)
            {
                return Sanitize(topMsg.GetString() ?? string.Empty, apiKey);
            }
        }
        catch
        {
        }

        return Sanitize(body, apiKey);
    }

    public static string Sanitize(string text, string? apiKey = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var cleaned = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (!string.IsNullOrWhiteSpace(apiKey) && apiKey.Length >= 6)
        {
            cleaned = cleaned.Replace(apiKey.Trim(), "[REDACTED]", StringComparison.Ordinal);
        }

        cleaned = SecretPattern.Replace(cleaned, "[REDACTED]");
        return cleaned.Length > 160 ? cleaned[..160] + "…" : cleaned;
    }

    private static bool HasCertificateError(HttpRequestException ex)
    {
        for (Exception? cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur is AuthenticationException)
            {
                return true;
            }
        }

        return false;
    }
}
