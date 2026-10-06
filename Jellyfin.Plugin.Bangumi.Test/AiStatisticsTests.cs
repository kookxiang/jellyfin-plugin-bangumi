using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.AI;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Test.Mock;
using Jellyfin.Plugin.Bangumi.Test.Util;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class AiStatisticsTests
{
    [DataTestMethod]
    [DataRow("OpenAI", "{\"prompt_tokens\":100,\"completion_tokens\":20,\"prompt_tokens_details\":{\"cached_tokens\":60,\"cache_write_tokens\":10}}")]
    [DataRow("Responses", "{\"input_tokens\":100,\"output_tokens\":20,\"input_tokens_details\":{\"cached_tokens\":60,\"cache_write_tokens\":10},\"output_tokens_details\":{\"reasoning_tokens\":15}}")]
    [DataRow("Anthropic", "{\"input_tokens\":30,\"output_tokens\":20,\"cache_read_input_tokens\":60,\"cache_creation_input_tokens\":10}")]
    public void ProtocolsNormalizeCacheUsageWithoutDoubleBilling(string format, string usage)
    {
        using var document = JsonDocument.Parse("{\"usage\":" + usage + "}");
        var result = AiUsage.Read(format, document.RootElement)!;
        Assert.AreEqual(100L, result.InputTokens);
        Assert.AreEqual(30L, result.OrdinaryInputTokens);
        Assert.AreEqual(60L, result.CachedInputTokens);
        Assert.AreEqual(10L, result.CacheCreationTokens);
        var (cost, hasPrice, complete) = AiCostCalculator.Calculate(result, Prices());
        Assert.AreEqual(0.000101m, cost);
        Assert.IsTrue(hasPrice && complete);
    }

    [TestMethod]
    public void MissingMalformedAndInconsistentUsageNeverBecomesACompleteEstimate()
    {
        foreach (var json in new[] { "{}", "{\"usage\":null}", "{\"usage\":{\"prompt_tokens\":-1}}", "{\"usage\":{\"prompt_tokens\":\"100\"}}", "{\"usage\":{\"prompt_tokens\":100,\"completion_tokens\":20,\"prompt_tokens_details\":{\"cached_tokens\":101}}}" })
        {
            using var document = JsonDocument.Parse(json);
            var usage = AiUsage.Read("OpenAI", document.RootElement);
            Assert.IsFalse(usage?.IsComplete == true);
            Assert.IsFalse(AiCostCalculator.Calculate(usage, Prices()).Complete);
        }
        using var legacy = JsonDocument.Parse("{\"usage\":{\"prompt_tokens\":100,\"completion_tokens\":20}}");
        Assert.IsTrue(AiUsage.Read("OpenAI", legacy.RootElement)!.IsComplete);
    }

    [TestMethod]
    public void BlankPricesAreUnpricedButExplicitZeroIsFreeAndPartialPricesStayPartial()
    {
        var usage = new AiUsage(100, 20, 60, 10);
        Assert.IsFalse(AiCostCalculator.Calculate(usage, new AiPricingConfiguration()).HasPrice);
        var free = AiCostCalculator.Calculate(usage, new AiPricingConfiguration { Input = 0, Output = 0, CachedInput = 0, CacheCreation = 0 });
        Assert.IsTrue(free.Complete);
        Assert.AreEqual(0m, free.Cost);
        var partial = AiCostCalculator.Calculate(usage, new AiPricingConfiguration { Output = 2 });
        Assert.AreEqual(0.00004m, partial.Cost);
        Assert.IsFalse(partial.Complete);
        Assert.ThrowsException<ArgumentException>(() => AiCostCalculator.Validate(new AiPricingConfiguration { Input = -1 }));
    }

    [TestMethod]
    public async Task ConcurrentAggregationPersistsGroupsAndHistoricalCostsAndResetSurvivesRestart()
    {
        var paths = Paths();
        using var store = new AiStatisticsStore(paths);
        var provider = Provider();
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => store.RecordAsync(provider, new AiUsage(100, 20, 60, 10))));
        provider.Pricing.Input = 10;
        provider.Name = "renamed";
        await store.RecordAsync(provider, new AiUsage(100, 20, 60, 10));
        provider.Model = "another-model";
        await store.RecordAsync(provider, null);
        provider.Id = "another-provider";
        await store.RecordAsync(provider, new AiUsage(5, 2, 0, 0));
        var snapshot = await store.GetAsync();
        Assert.AreEqual(3, snapshot.Rows.Count);
        var row = snapshot.Rows[0];
        Assert.AreEqual(21L, row.Calls);
        Assert.AreEqual(2100L, row.InputTokens);
        Assert.AreEqual(20 * 0.000101m + 0.000371m, row.EstimatedCost);
        Assert.AreEqual("renamed", row.ProviderName);
        Assert.AreEqual(1L, snapshot.Rows[1].UnknownUsageCalls);
        snapshot.Rows.Clear();
        Assert.AreEqual(3, (await store.GetAsync()).Rows.Count);
        using var reloaded = new AiStatisticsStore(paths);
        Assert.AreEqual(row.EstimatedCost, (await reloaded.GetAsync()).Rows[0].EstimatedCost);
        var file = await File.ReadAllTextAsync(Path.Join(paths.DataPath, "bangumi", "ai-usage.json"));
        Assert.IsFalse(file.Contains("test-only-key") || file.Contains("ApiKey") || file.Contains("Pricing"));
        await store.ClearAsync();
        using var cleared = new AiStatisticsStore(paths);
        Assert.AreEqual(0, (await cleared.GetAsync()).Rows.Count);
    }

    [TestMethod]
    public async Task TransportSnapshotsConfigurationAndRecordsGenericCallsAndUnknownFailures()
    {
        using var store = new AiStatisticsStore(Paths());
        var provider = Provider();
        using var handler = new Handler(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.AreEqual("custom prompt", body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
            provider.Model = "changed-after-send";
            provider.Pricing.Input = 999;
            return Reply("{\"choices\":[{\"message\":{\"content\":\"reply\"}}],\"usage\":{\"prompt_tokens\":100,\"completion_tokens\":20}}");
        });
        using var http = new HttpClient(handler);
        var result = await Client(http, store).SendAsync(provider, "custom prompt", CancellationToken.None);
        Assert.AreEqual("reply", result.Text);
        Assert.AreEqual(100L, result.Usage!.InputTokens);
        var row = (await store.GetAsync()).Rows.Single();
        Assert.AreEqual("model", row.Model);
        Assert.AreEqual(0.00014m, row.EstimatedCost);
        using var timeout = new Handler((_, _) => throw new TaskCanceledException());
        using var timeoutHttp = new HttpClient(timeout);
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => Client(timeoutHttp, store).TestAsync(provider, CancellationToken.None));
        Assert.AreEqual(1L, (await store.GetAsync()).Rows[1].UnknownUsageCalls);
        provider.Pricing.Input = -1;
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Client(timeoutHttp, store).TestAsync(provider, CancellationToken.None));
        Assert.AreEqual(2L, (await store.GetAsync()).Rows.Sum(item => item.Calls));
    }

    [TestMethod]
    public async Task UsageIsRecordedForEmptyRepliesAndHttpFailuresWhenReturned()
    {
        using var store = new AiStatisticsStore(Paths());
        using var handler = new Handler((_, _) => Task.FromResult(Reply("{\"usage\":{\"prompt_tokens\":100,\"completion_tokens\":20}}")));
        using var http = new HttpClient(handler);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Client(http, store).TestAsync(Provider(), CancellationToken.None));
        using var failed = new Handler((_, _) => Task.FromResult(Reply("{\"usage\":{\"prompt_tokens\":100,\"completion_tokens\":20}}", HttpStatusCode.BadRequest)));
        using var failedHttp = new HttpClient(failed);
        await Assert.ThrowsExceptionAsync<HttpRequestException>(() => Client(failedHttp, store).TestAsync(Provider(), CancellationToken.None));
        var row = (await store.GetAsync()).Rows.Single();
        Assert.AreEqual(2L, row.Calls);
        Assert.AreEqual(0L, row.UnknownUsageCalls);
        Assert.AreEqual(0.00028m, row.EstimatedCost);
    }

    [TestMethod]
    public async Task UnreadableStatisticsDoNotOverwriteHistoryOrBreakModelReplies()
    {
        var paths = Paths();
        var directory = Path.Join(paths.DataPath, "bangumi");
        Directory.CreateDirectory(directory);
        var file = Path.Join(directory, "ai-usage.json");
        await File.WriteAllTextAsync(file, "broken-json");
        using var store = new AiStatisticsStore(paths);
        using var handler = new Handler((_, _) => Task.FromResult(Reply("{\"choices\":[{\"message\":{\"content\":\"reply\"}}]}")));
        using var http = new HttpClient(handler);
        Assert.AreEqual("reply", (await Client(http, store).TestAsync(Provider(), CancellationToken.None)).Text);
        Assert.AreEqual("broken-json", await File.ReadAllTextAsync(file));
        await Assert.ThrowsExceptionAsync<JsonException>(() => store.GetAsync());
        await store.ClearAsync();
        Assert.AreEqual(0, (await store.GetAsync()).Rows.Count);
    }

    private static MockedApplicationPaths Paths() => new(FakePath.Create("ai-statistics-" + Guid.NewGuid()));
    private static AiPricingConfiguration Prices() => new() { Input = 1, Output = 2, CachedInput = 0.1m, CacheCreation = 2.5m };
    private static AiProviderConfiguration Provider() => new() { Id = "provider", Name = "Local", Model = "model", ApiKey = "test-only-key", Pricing = Prices() };
    private static AiProviderClient Client(HttpClient http, AiStatisticsStore store) => new(http, store, NullLogger<AiProviderClient>.Instance);
    private static HttpResponseMessage Reply(string json, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(json) };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
