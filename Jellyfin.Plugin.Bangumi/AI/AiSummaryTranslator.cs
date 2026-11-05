using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bangumi.AI;

public sealed record SummaryTranslationRequest(int SubjectId, string? SourceText, string OriginalTitle, string? LocalizedTitle);

public interface IAiSummaryTranslator
{
    /// <summary>Returns null when disabled, unnecessary, or unsuccessful. Caller retains the source.</summary>
    Task<string?> TryTranslateSummaryAsync(SummaryTranslationRequest request, CancellationToken token);
    Task ClearCacheAsync(CancellationToken token);
}

/// <summary>Optional metadata post-processing; never changes the source Bangumi objects or API cache.</summary>
public sealed class AiSummaryTranslator : IAiSummaryTranslator, IDisposable
{
    private readonly AiProviderClient _client;
    private readonly ILogger<AiSummaryTranslator> _logger;
    private readonly Func<AiConfiguration> _configuration;
    private readonly string _cacheDirectory;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 8 * 1024 * 1024 });
    private sealed record CachedSummary(string? Text, DateTimeOffset CreatedAt, long Generation);

    private readonly ConcurrentDictionary<(string Key, long Generation), Lazy<Task<string?>>> _pending = new();
    private readonly SemaphoreSlim _fileGate = new(1, 1);
    private long _generation;
    private readonly SemaphoreSlim _gate = new(2, 2);

    public AiSummaryTranslator(AiProviderClient client, ILogger<AiSummaryTranslator> logger, IApplicationPaths paths)
        : this(client, logger, paths, () => Plugin.Instance!.Configuration.Ai) { }

    internal AiSummaryTranslator(AiProviderClient client, ILogger<AiSummaryTranslator> logger, IApplicationPaths paths, Func<AiConfiguration> configuration)
    {
        _client = client;
        _logger = logger;
        _configuration = configuration;
        _cacheDirectory = Path.Join(paths.DataPath, "bangumi", "ai-summary-cache", "v1");
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

        // Reuse across subjects and credential/provider renames. Only the model request determines the result.
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            provider.Endpoint, provider.Model, provider.Format, prompt
        }))));
        var generation = Interlocked.Read(ref _generation);
        if (TryGetMemoryCache(key, generation, out var cached)) return cached!.Text;
        var pending = _pending.GetOrAdd((key, generation), _ => new Lazy<Task<string?>>(() => TranslateAsync(key, generation, provider, prompt, request.SubjectId)));
        // A cancelled refresh stops waiting without cancelling another refresh sharing the same call.
        return await pending.Value.WaitAsync(token);
    }

    private bool IsExpired(DateTimeOffset createdAt)
    {
        var days = _configuration().CacheDurationDays;
        return days is > 0 and <= 36500 && DateTimeOffset.UtcNow - createdAt >= TimeSpan.FromDays(days);
    }

    private bool TryGetMemoryCache(string key, long generation, out CachedSummary? cached)
    {
        return _cache.TryGetValue(key, out cached) && cached != null && cached.Generation == generation
            && (cached.Text == null ? DateTimeOffset.UtcNow - cached.CreatedAt < TimeSpan.FromMinutes(1) : !IsExpired(cached.CreatedAt));
    }

    private async Task<string?> TranslateAsync(string key, long generation, AiProviderConfiguration provider, string prompt, int subjectId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        CachedSummary? entry = null;
        try
        {
            // Keep the original file timestamp when warming memory so reads do not renew its lifetime.
            entry = await ReadFileCacheAsync(key, generation, timeout.Token);
            if (entry != null) return entry.Text;
            await _gate.WaitAsync(timeout.Token);
            try
            {
                if (TryGetMemoryCache(key, generation, out var cached))
                {
                    entry = cached;
                    return entry!.Text;
                }
                var result = await _client.SendAsync(provider, prompt, timeout.Token, 4096);
                var translated = result.Text.ToMarkdown();
                if (IsUsable(translated)) entry = new CachedSummary(translated, DateTimeOffset.UtcNow, generation);
                else _logger.LogWarning("AI summary translation returned unusable text for subject {SubjectId}.", subjectId);
            }
            finally { _gate.Release(); }
            if (entry != null) await WriteFileCacheAsync(key, entry, timeout.Token);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException
            or ArgumentException or InvalidOperationException or OverflowException)
        {
            _logger.LogWarning("AI summary translation failed for subject {SubjectId} ({ErrorType}); keeping original summary.", subjectId, error.GetType().Name);
        }
        finally
        {
            entry ??= new CachedSummary(null, DateTimeOffset.UtcNow, generation);
            if (generation == Interlocked.Read(ref _generation))
                _cache.Set(key, entry, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = entry.Text == null ? TimeSpan.FromMinutes(1) : TimeSpan.FromDays(7),
                    Size = 256 + (entry.Text?.Length ?? 0) * 2
                });
            _pending.TryRemove((key, generation), out _);
        }
        return entry?.Text;
    }

    public async Task ClearCacheAsync(CancellationToken token)
    {
        await _fileGate.WaitAsync(token);
        try
        {
            // Old flights can finish for their callers, but cannot refill cleared caches or be joined by new calls.
            Interlocked.Increment(ref _generation);
            _cache.Clear();
            try { Directory.Delete(_cacheDirectory, true); }
            catch (DirectoryNotFoundException) when (!File.Exists(_cacheDirectory)) { }
        }
        finally { _fileGate.Release(); }
    }

    private async Task<CachedSummary?> ReadFileCacheAsync(string key, long generation, CancellationToken token)
    {
        await _fileGate.WaitAsync(token);
        try
        {
            if (generation != Interlocked.Read(ref _generation)) return null;
            var path = Path.Join(_cacheDirectory, key + ".txt");
            var file = new FileInfo(path);
            if (file.Length > 128 * 1024 || IsExpired(file.LastWriteTimeUtc)) return null;
            var text = await File.ReadAllTextAsync(path, token);
            return IsUsable(text) ? new CachedSummary(text, file.LastWriteTimeUtc, generation) : null;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("AI summary file cache could not be read ({ErrorType}).", error.GetType().Name);
            return null;
        }
        finally { _fileGate.Release(); }
    }

    private async Task WriteFileCacheAsync(string key, CachedSummary entry, CancellationToken token)
    {
        await _fileGate.WaitAsync(token);
        var temporaryPath = Path.Join(_cacheDirectory, key + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            if (entry.Generation != Interlocked.Read(ref _generation)) return;
            Directory.CreateDirectory(_cacheDirectory);
            await File.WriteAllTextAsync(temporaryPath, entry.Text, token);
            File.SetLastWriteTimeUtc(temporaryPath, entry.CreatedAt.UtcDateTime);
            File.Move(temporaryPath, Path.Join(_cacheDirectory, key + ".txt"), true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            _logger.LogWarning("AI summary file cache could not be saved ({ErrorType}); using translated summary.", error.GetType().Name);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning("AI summary file cache temporary file could not be cleaned up ({ErrorType}).", error.GetType().Name);
            }
            _fileGate.Release();
        }
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
        _fileGate.Dispose();
    }
}
