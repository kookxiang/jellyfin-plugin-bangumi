using System;
using System.Text.Json;
using Jellyfin.Plugin.Bangumi.Configuration;

namespace Jellyfin.Plugin.Bangumi.AI;

public sealed record AiResult(string Text, AiUsage? Usage);

/// <summary>Input is the total, including cache reads and writes; missing usage is never guessed.</summary>
public sealed record AiUsage(long? InputTokens, long? OutputTokens, long? CachedInputTokens, long? CacheCreationTokens)
{
    public long? OrdinaryInputTokens => InputTokens.HasValue && CachedInputTokens.HasValue && CacheCreationTokens.HasValue
        && CachedInputTokens <= InputTokens && CacheCreationTokens <= InputTokens - CachedInputTokens
            ? InputTokens - CachedInputTokens - CacheCreationTokens : null;

    public bool IsComplete => OrdinaryInputTokens.HasValue && OutputTokens.HasValue;

    internal static AiUsage? Read(string format, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("usage", out var usage)
            || usage.ValueKind != JsonValueKind.Object) return null;

        if (format == "Anthropic")
        {
            var ordinary = Count(usage, "input_tokens");
            var cached = Count(usage, "cache_read_input_tokens", 0);
            var written = Count(usage, "cache_creation_input_tokens", 0);
            long? total = ordinary.HasValue && cached.HasValue && written.HasValue
                && cached <= long.MaxValue - ordinary && written <= long.MaxValue - ordinary - cached
                    ? ordinary + cached + written : null;
            return new AiUsage(total, Count(usage, "output_tokens"), cached, written);
        }

        var chat = format == "OpenAI";
        var input = Count(usage, chat ? "prompt_tokens" : "input_tokens");
        var output = Count(usage, chat ? "completion_tokens" : "output_tokens");
        var detailName = chat ? "prompt_tokens_details" : "input_tokens_details";
        if (!usage.TryGetProperty(detailName, out var details) || details.ValueKind == JsonValueKind.Null)
            return new AiUsage(input, output, 0, 0);
        return new AiUsage(input, output, Count(details, "cached_tokens", 0), Count(details, "cache_write_tokens", 0));
    }

    private static long? Count(JsonElement element, string name, long? missing = null)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(name, out var value)) return missing;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var count) && count >= 0 ? count : null;
    }
}

internal static class AiCostCalculator
{
    internal static void Validate(AiPricingConfiguration pricing)
    {
        foreach (var price in new[] { pricing.Input, pricing.Output, pricing.CachedInput, pricing.CacheCreation })
            if (price < 0 || price > 1_000_000_000m)
                throw new ArgumentException("价格必须在 0 到 1,000,000,000 美元 / 百万 Token 之间。");
    }

    internal static (decimal Cost, bool HasPrice, bool Complete) Calculate(AiUsage? usage, AiPricingConfiguration pricing)
    {
        var hasPrice = pricing.Input.HasValue || pricing.Output.HasValue || pricing.CachedInput.HasValue || pricing.CacheCreation.HasValue;
        var complete = usage?.IsComplete == true && hasPrice;
        decimal cost = 0;
        foreach (var (tokens, price) in new[]
        {
            (usage?.OrdinaryInputTokens, pricing.Input), (usage?.OutputTokens, pricing.Output),
            (usage?.CachedInputTokens, pricing.CachedInput), (usage?.CacheCreationTokens, pricing.CacheCreation)
        })
        {
            if (!tokens.HasValue || tokens > 0 && !price.HasValue) complete = false;
            if (tokens.HasValue && price.HasValue) cost += tokens.Value / 1_000_000m * price.Value;
        }
        return (cost, hasPrice && usage is not null, complete);
    }
}
