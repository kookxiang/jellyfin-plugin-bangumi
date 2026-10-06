using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bangumi.AI;

/// <summary>Shared text transport and usage collection, independent of Bangumi metadata and parsers.</summary>
public class AiProviderClient(HttpClient client, AiStatisticsStore statistics, ILogger<AiProviderClient> logger)
{
    internal const string TestMessage = "你好，请简短回复。";

    public Task<AiResult> TestAsync(AiProviderConfiguration provider, CancellationToken token)
        => SendAsync(provider, TestMessage, token);

    public async Task<AiResult> SendAsync(AiProviderConfiguration provider, string prompt, CancellationToken token)
    {
        // Snapshot prices and identity before the asynchronous request starts.
        provider = new AiProviderConfiguration
        {
            Id = provider.Id, Name = provider.Name, Endpoint = provider.Endpoint, Model = provider.Model,
            ApiKey = provider.ApiKey, Format = provider.Format,
            Pricing = new AiPricingConfiguration
            {
                Input = provider.Pricing?.Input, Output = provider.Pricing?.Output,
                CachedInput = provider.Pricing?.CachedInput, CacheCreation = provider.Pricing?.CacheCreation
            }
        };
        AiCostCalculator.Validate(provider.Pricing);
        using var request = CreateRequest(provider, prompt);
        token.ThrowIfCancellationRequested();
        AiUsage? usage = null;
        try
        {
            using var response = await client.SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
            {
                // Some gateways include billable usage in failed responses. Never expose their bodies.
                try
                {
                    using var failure = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                    usage = AiUsage.Read(provider.Format, failure.RootElement);
                }
                catch (JsonException) { }
                var reason = (int)response.StatusCode switch
                {
                    401 or 403 => "身份验证失败，请检查 API Key 和模型权限。",
                    404 => "接口或模型不存在，请检查 Endpoint、接口格式和 Model。",
                    429 => "请求被限流或额度不足。",
                    >= 300 and < 400 => "接口返回了重定向，请填写最终 API 地址。",
                    _ => "模型服务拒绝请求，请检查接口格式和模型配置。"
                };
                // Upstream bodies may echo credentials or request headers.
                throw new HttpRequestException($"HTTP {(int)response.StatusCode}：{reason}", null, response.StatusCode);
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            usage = AiUsage.Read(provider.Format, json.RootElement);
            var text = ReadText(provider.Format, json.RootElement);
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("接口返回成功，但没有可读取的文本回复。");
            return new AiResult(text.Trim(), usage);
        }
        finally
        {
            try { await statistics.RecordAsync(provider, usage); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or OverflowException)
            {
                // Statistics failure must not discard a usable model reply or replace a transport error.
                logger.LogWarning("AI usage statistics could not be saved ({ErrorType}).", error.GetType().Name);
            }
        }
    }

    private static HttpRequestMessage CreateRequest(AiProviderConfiguration provider, string prompt)
    {
        if (string.IsNullOrWhiteSpace(provider.Model))
            throw new ArgumentException("请填写 Model。", nameof(provider));
        if (provider.ApiKey?.Contains('\r') == true || provider.ApiKey?.Contains('\n') == true)
            throw new ArgumentException("API Key 不能包含换行。", nameof(provider));

        var endpoint = BuildEndpoint(provider.Endpoint, provider.Format);
        var model = provider.Model.Trim();
        var messages = new[] { new { role = "user", content = prompt } };
        object body = provider.Format switch
        {
            "OpenAI" => new { model, messages, stream = false },
            "Responses" => new { model, input = prompt, store = false, stream = false, max_output_tokens = 1024 },
            "Anthropic" => new { model, messages, stream = false, max_tokens = 256 },
            _ => throw new ArgumentException("不支持的接口格式。", nameof(provider))
        };
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            if (provider.Format == "Anthropic") request.Headers.Add("x-api-key", provider.ApiKey.Trim());
            else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey.Trim());
        }
        if (provider.Format == "Anthropic") request.Headers.Add("anthropic-version", "2023-06-01");
        return request;
    }

    internal static Uri BuildEndpoint(string endpoint, string format)
    {
        if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Endpoint 必须是 HTTP 或 HTTPS 地址，且不能包含账号、查询参数或片段。", nameof(endpoint));
        var suffix = format switch
        {
            "OpenAI" => "chat/completions",
            "Responses" => "responses",
            "Anthropic" => "messages",
            _ => throw new ArgumentException("不支持的接口格式。", nameof(format))
        };
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith('/' + suffix, StringComparison.Ordinal)) return new Uri(uri.GetLeftPart(UriPartial.Path).TrimEnd('/'));
        if (path.EndsWith("/chat/completions", StringComparison.Ordinal)
            || path.EndsWith("/responses", StringComparison.Ordinal)
            || path.EndsWith("/messages", StringComparison.Ordinal))
            throw new ArgumentException("完整接口地址与所选接口格式不匹配。", nameof(endpoint));
        return new Uri(uri.GetLeftPart(UriPartial.Authority) + (path.Length == 0 ? "/v1" : path) + '/' + suffix);
    }

    internal static string ReadText(string format, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return "";
        if (format == "OpenAI" && root.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0
            && choices[0].ValueKind == JsonValueKind.Object && choices[0].TryGetProperty("message", out var message)
            && message.ValueKind == JsonValueKind.Object && message.TryGetProperty("content", out var content))
            return content.ValueKind == JsonValueKind.String ? content.GetString() ?? "" : ReadBlocks(content, "text");
        if (format == "Anthropic" && root.TryGetProperty("content", out var blocks))
            return ReadBlocks(blocks, "text");
        if (format == "Responses" && root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            var texts = new List<string>();
            foreach (var item in output.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("type", out var type)
                    && type.ValueKind == JsonValueKind.String && type.GetString() == "message"
                    && item.TryGetProperty("content", out var responseContent))
                    texts.Add(ReadBlocks(responseContent, "output_text"));
            }
            return string.Join("\n", texts);
        }
        return "";
    }

    private static string ReadBlocks(JsonElement blocks, string expectedType)
    {
        if (blocks.ValueKind != JsonValueKind.Array) return "";
        var texts = new List<string>();
        foreach (var block in blocks.EnumerateArray())
        {
            if (block.ValueKind == JsonValueKind.Object && block.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String && type.GetString() == expectedType
                && block.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                texts.Add(text.GetString() ?? "");
        }
        return string.Join("\n", texts);
    }
}
