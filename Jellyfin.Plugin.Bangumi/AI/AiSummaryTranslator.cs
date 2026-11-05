using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bangumi.AI;

public sealed record SummaryTranslationRequest(int SubjectId, string? SourceText, string OriginalTitle, string? LocalizedTitle);

public interface IAiSummaryTranslator
{
    /// <summary>Returns null when disabled, unnecessary, or unsuccessful. Caller retains the source.</summary>
    Task<string?> TryTranslateSummaryAsync(SummaryTranslationRequest request, CancellationToken token);
}

/// <summary>Optional metadata post-processing; never changes the source Bangumi objects or API cache.</summary>
public sealed class AiSummaryTranslator : IAiSummaryTranslator, IDisposable
{
    private readonly AiProviderClient _client;
    private readonly ILogger<AiSummaryTranslator> _logger;
    private readonly Func<AiConfiguration> _configuration;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 8 * 1024 * 1024 });
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _pending = new();
    private readonly SemaphoreSlim _gate = new(2, 2);

    public AiSummaryTranslator(AiProviderClient client, ILogger<AiSummaryTranslator> logger)
        : this(client, logger, () => Plugin.Instance!.Configuration.Ai) { }

    internal AiSummaryTranslator(AiProviderClient client, ILogger<AiSummaryTranslator> logger, Func<AiConfiguration> configuration)
    {
        _client = client;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<string?> TryTranslateSummaryAsync(SummaryTranslationRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var configuration = _configuration();
        var feature = configuration.SummaryTranslation;
        if (!feature.Enabled || string.IsNullOrWhiteSpace(request.SourceText) || request.SourceText.Length > 16000
            || !ShouldTranslate(request.SourceText)) return null;
        var provider = configuration.Providers.FirstOrDefault(item => item.Id == feature.ProviderId);
        if (provider == null || string.IsNullOrWhiteSpace(provider.Id)) return null;
        provider = AiProviderClient.Snapshot(provider);

        if (feature.Prompt.Length > 32000) return null;
        var prompt = RenderPrompt(feature.Prompt, request);
        if (prompt == null || prompt.Length > 32000) return null;

        // Hash every output-affecting input, including credentials, without retaining them in cache keys.
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            provider.Id, provider.Endpoint, provider.Model, provider.Format, provider.ApiKey, prompt
        }))));
        if (_cache.TryGetValue(key, out string? cached)) return cached;
        var pending = _pending.GetOrAdd(key, _ => new Lazy<Task<string?>>(() => TranslateAsync(key, provider, prompt, request.SubjectId)));
        // A cancelled refresh stops waiting without cancelling another refresh sharing the same call.
        return await pending.Value.WaitAsync(token);
    }

    private async Task<string?> TranslateAsync(string key, AiProviderConfiguration provider, string prompt, int subjectId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        string? text = null;
        try
        {
            await _gate.WaitAsync(timeout.Token);
            try
            {
                // A previous flight may have completed between the caller's cache lookup and GetOrAdd.
                if (_cache.TryGetValue(key, out string? cached))
                {
                    text = cached;
                    return text;
                }
                var result = await _client.SendAsync(provider, prompt, timeout.Token, 4096);
                if (IsUsable(result.Text)) text = result.Text.ToMarkdown();
                else _logger.LogWarning("AI summary translation returned unusable text for subject {SubjectId}.", subjectId);
            }
            finally { _gate.Release(); }
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException
            or ArgumentException or InvalidOperationException or OverflowException)
        {
            // Never log upstream bodies, prompts, endpoints, or credentials.
            _logger.LogWarning("AI summary translation failed for subject {SubjectId} ({ErrorType}); keeping original summary.", subjectId, error.GetType().Name);
        }
        finally
        {
            _cache.Set(key, text, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = text == null ? TimeSpan.FromMinutes(1) : TimeSpan.FromDays(7),
                Size = 256 + (text?.Length ?? 0) * 2
            });
            _pending.TryRemove(key, out _);
        }
        return text;
    }

    internal static string? RenderPrompt(string template, SummaryTranslationRequest request)
    {
        IReadOnlyDictionary<string, string?> variables = new Dictionary<string, string?>
        {
            ["source_text"] = request.SourceText,
            ["original_title"] = request.OriginalTitle,
            ["localized_title"] = request.LocalizedTitle
        };
        var valid = true;
        var hasSource = false;
        // Replace in one pass: template-like text inside metadata must remain literal.
        var prompt = Regex.Replace(template, @"\{\{\s*([^{}]*?)\s*\}\}", match =>
        {
            var name = match.Groups[1].Value.Trim();
            if (name == "source_text") hasSource = true;
            if (variables.TryGetValue(name, out var value)) return value ?? "";
            valid = false;
            return "";
        });
        return valid && hasSource ? prompt : null;
    }

    internal static bool ShouldTranslate(string source)
    {
        // URL/domain names and markup are not evidence of a foreign-language synopsis.
        var plain = Regex.Replace(source, @"https?://\S+|<[^>]+>", "");
        var originalMarker = plain.IndexOf("[简介原文]", StringComparison.Ordinal);
        if (originalMarker > 0 && plain[..originalMarker].EnumerateRunes()
                .Count(rune => rune.Value is >= 0x3400 and <= 0x9fff or >= 0x20000 and <= 0x323af) >= 8)
            return false;
        var foreign = false;
        foreach (var line in plain.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var han = 0;
            var kanaOrHangul = 0;
            var otherLetters = 0;
            foreach (var rune in line.EnumerateRunes())
            {
                var value = rune.Value;
                if (value is >= 0x3400 and <= 0x9fff or >= 0x20000 and <= 0x323af) han++;
                else if (value is >= 0x3040 and <= 0x30ff or >= 0xff66 and <= 0xff9d
                    or >= 0xac00 and <= 0xd7af or >= 0x1100 and <= 0x11ff or >= 0x3130 and <= 0x318f) kanaOrHangul++;
                else if (Rune.IsLetter(rune)) otherLetters++;
            }
            // Preserve bilingual summaries with a substantial Chinese line. Pure Han is ambiguous.
            if (han >= 8 && kanaOrHangul * 4 <= han && han * 2 >= otherLetters) return false;
            foreign |= kanaOrHangul >= 2 || otherLetters >= 8 && otherLetters > han * 2;
        }
        return foreign;
    }

    internal static bool IsUsable(string text) => !string.IsNullOrWhiteSpace(text) && text.Length <= 32000
        && text.EnumerateRunes().Any(Rune.IsLetter)
        && !text.StartsWith("```", StringComparison.Ordinal)
        && !text.StartsWith('{') && !text.StartsWith('[')
        && !text.Contains("<think", StringComparison.OrdinalIgnoreCase)
        && !text.Contains("<script", StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        _cache.Dispose();
        _gate.Dispose();
    }
}
