using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.Bangumi.AI;

public sealed class AiStatistics
{
    public DateTimeOffset? UpdatedAt { get; set; }
    public List<AiModelStatistics> Rows { get; set; } = [];
}

public sealed class AiModelStatistics
{
    public string ProviderId { get; set; } = "";
    public string ProviderName { get; set; } = "";
    public string Model { get; set; } = "";
    public long Calls { get; set; }
    public long UnknownUsageCalls { get; set; }
    public long UnpricedCalls { get; set; }
    public long PricedCalls { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CachedInputTokens { get; set; }
    public long CacheCreationTokens { get; set; }
    public decimal EstimatedCost { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }
}

/// <summary>Only aggregate counters are persisted, independently of plugin settings and prompt content.</summary>
public sealed class AiStatisticsStore : IDisposable
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AiStatistics _statistics = new();
    private bool _loaded;

    public AiStatisticsStore(IApplicationPaths paths)
    {
        _path = Path.Join(paths.DataPath, "bangumi", "ai-usage.json");
    }

    public async Task<AiStatistics> GetAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            await LoadAsync();
            return Clone();
        }
        finally { _gate.Release(); }
    }

    public async Task RecordAsync(AiProviderConfiguration provider, AiUsage? usage)
    {
        await _gate.WaitAsync();
        try
        {
            await LoadAsync();
            var next = Clone();
            var id = string.IsNullOrWhiteSpace(provider.Id) ? "endpoint:" + provider.Endpoint : provider.Id;
            var model = provider.Model.Trim();
            var row = next.Rows.FirstOrDefault(item => item.ProviderId == id && item.Model == model);
            if (row is null)
            {
                row = new AiModelStatistics { ProviderId = id, Model = model };
                next.Rows.Add(row);
            }
            row.ProviderName = provider.Name;
            var (cost, hasPrice, complete) = AiCostCalculator.Calculate(usage, provider.Pricing);
            checked
            {
                row.Calls++;
                if (usage?.IsComplete != true) row.UnknownUsageCalls++;
                row.InputTokens += usage?.InputTokens ?? 0;
                row.OutputTokens += usage?.OutputTokens ?? 0;
                row.CachedInputTokens += usage?.CachedInputTokens ?? 0;
                row.CacheCreationTokens += usage?.CacheCreationTokens ?? 0;
                row.EstimatedCost += cost;
                if (hasPrice) row.PricedCalls++;
                if (!complete) row.UnpricedCalls++;
            }
            row.LastUsedAt = DateTimeOffset.UtcNow;
            next.UpdatedAt = row.LastUsedAt;
            await SaveAsync(next);
        }
        finally { _gate.Release(); }
    }

    public async Task<AiStatistics> ClearAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            // Explicit reset can also recover an unreadable statistics file.
            await SaveAsync(new AiStatistics { UpdatedAt = DateTimeOffset.UtcNow });
            return Clone();
        }
        finally { _gate.Release(); }
    }

    private async Task LoadAsync()
    {
        if (_loaded) return;
        if (File.Exists(_path))
        {
            var saved = JsonSerializer.Deserialize<AiStatistics>(await File.ReadAllTextAsync(_path));
            if (saved?.Rows is null || saved.Rows.Any(row => row is null || row.ProviderId is null || row.Model is null
                || row.Calls < 0 || row.InputTokens < 0 || row.OutputTokens < 0 || row.EstimatedCost < 0
                || row.CachedInputTokens < 0 || row.CacheCreationTokens < 0 || row.UnknownUsageCalls < 0
                || row.UnpricedCalls < 0 || row.PricedCalls < 0))
                throw new JsonException("Invalid AI statistics.");
            _statistics = saved;
        }
        _loaded = true;
    }

    private AiStatistics Clone() => JsonSerializer.Deserialize<AiStatistics>(JsonSerializer.Serialize(_statistics))!;

    private async Task SaveAsync(AiStatistics next)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await File.WriteAllTextAsync(_path + ".tmp", JsonSerializer.Serialize(next));
        File.Move(_path + ".tmp", _path, true);
        _statistics = next;
        _loaded = true;
    }

    public void Dispose() => _gate.Dispose();
}
