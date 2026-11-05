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
using Jellyfin.Plugin.Bangumi.Providers;
using Jellyfin.Plugin.Bangumi.Test.Mock;
using Jellyfin.Plugin.Bangumi.Test.Util;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class AiSummaryTranslationTests
{
    private const string Japanese = "高校生の主人公は、仲間たちと共に異世界へ旅に出る。\n新しい冒険が始まる。";
    private static readonly SummaryTranslationRequest Request = new(123, Japanese, "作品原名", "作品译名");

    [DataTestMethod]
    [DataRow(Japanese, true)]
    [DataRow("ｺｳｺｳｾｲの物語", true)]
    [DataRow("A young adventurer travels to another world with her friends.", true)]
    [DataRow("소년은 친구들과 함께 모험을 떠난다.", true)]
    [DataRow("这是一个少年和朋友们一起在异世界冒险的故事。", false)]
    [DataRow("這是一個少年和朋友們一起在異世界冒險的故事。", false)]
    [DataRow("这是一个名叫マコト的少年和朋友们一起在异世界冒险的故事。", false)]
    [DataRow("这是一个少年和朋友们一起冒险的故事。\n" + Japanese, false)]
    [DataRow("", false)]
    [DataRow("https://example.test/anime/123", false)]
    [DataRow("<a href='https://example.test'>少年冒险</a>", false)]
    [DataRow("異世界冒険物語", false)]
    public void LocalDetectionIsConservativeAndHandlesJapaneseHan(string source, bool expected)
        => Assert.AreEqual(expected, AiSummaryTranslator.ShouldTranslate(source));

    [TestMethod]
    public async Task BilingualOriginalMarkerSkipsModelEvenWithJapaneseNamesInChinese()
    {
        // Same layout as https://bgm.tv/subject/621073; synthetic text avoids copying the synopsis.
        const string summary = "少年マコト和伙伴们一起前往异世界，开始了全新的旅程。\n[简介原文]\n" + Japanese;
        using var fixture = new Fixture();
        Assert.IsNull(await fixture.Translator.TryTranslateSummaryAsync(Request with { SourceText = summary.ToMarkdown() }, CancellationToken.None));
        Assert.AreEqual(0, fixture.Calls);
        // The marker alone must not suppress a synopsis which has no translated section.
        Assert.IsTrue(AiSummaryTranslator.ShouldTranslate("[简介原文]\n" + Japanese));
    }

    [TestMethod]
    public async Task DisabledMissingProviderEmptyOrChineseSummaryNeverCallsModel()
    {
        using var fixture = new Fixture();
        fixture.Configuration.SummaryTranslation.Enabled = false;
        Assert.IsNull(await fixture.Translate());
        fixture.Configuration.SummaryTranslation.Enabled = true;
        fixture.Configuration.SummaryTranslation.ProviderId = "missing";
        Assert.IsNull(await fixture.Translate());
        fixture.Configuration.SummaryTranslation.ProviderId = "provider";
        foreach (var source in new[] { null, "", "这是一个少年在异世界冒险的故事。", new string('a', 16001) })
            Assert.IsNull(await fixture.Translator.TryTranslateSummaryAsync(Request with { SourceText = source }, CancellationToken.None));
        Assert.AreEqual(0, fixture.Calls);
    }

    [TestMethod]
    public void PromptRenderingRejectsUnknownOrMissingVariablesAndDoesNotExpandMetadata()
    {
        Assert.IsNull(AiSummaryTranslator.RenderPrompt("Translate to Chinese", Request));
        Assert.IsNull(AiSummaryTranslator.RenderPrompt("{{source_text}} {{typo}}", Request));
        Assert.AreEqual("{{source_text}}\n\n" + Japanese,
            AiSummaryTranslator.RenderPrompt("{{ original_title }}\n{{localized_title}}\n{{source_text}}",
                Request with { OriginalTitle = "{{source_text}}", LocalizedTitle = null }));
    }

    [TestMethod]
    public async Task TranslationUsesSelectedProviderContextAndStatisticsThenCachesReply()
    {
        using var fixture = new Fixture();
        Assert.AreEqual("少年和伙伴们一起踏上冒险旅程。", await fixture.Translate());
        StringAssert.Contains(fixture.LastPrompt, Japanese);
        StringAssert.Contains(fixture.LastPrompt, Request.OriginalTitle);
        StringAssert.Contains(fixture.LastPrompt, Request.LocalizedTitle!);
        Assert.AreEqual("少年和伙伴们一起踏上冒险旅程。", await fixture.Translate());
        Assert.AreEqual(1, fixture.Calls);
        Assert.AreEqual(1L, (await fixture.Statistics.GetAsync()).Rows.Single().Calls);
    }

    [DataTestMethod]
    [DataRow("OpenAI", "{\"choices\":[{\"message\":{\"content\":\"翻译后的简介。\"},\"finish_reason\":\"stop\"}]}")]
    [DataRow("Responses", "{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"翻译后的简介。\"}]}]}")]
    [DataRow("Anthropic", "{\"stop_reason\":\"end_turn\",\"content\":[{\"type\":\"text\",\"text\":\"翻译后的简介。\"}]}")]
    public async Task TranslationWorksWithAllConfiguredProtocols(string format, string reply)
    {
        using var fixture = new Fixture(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            if (format != "OpenAI")
                Assert.IsTrue(body.RootElement.GetProperty(format == "Responses" ? "max_output_tokens" : "max_tokens").GetInt32() >= 4096,
                    "Translation needs more output room than the short connection probe.");
            return reply;
        });
        fixture.Configuration.Providers[0].Format = format;
        Assert.AreEqual("翻译后的简介。", await fixture.Translate());
    }

    [DataTestMethod]
    [DataRow("Missing source")]
    [DataRow("{{source_text}} {{unknown}}")]
    public async Task InvalidPromptDoesNotSendRequest(string prompt)
    {
        using var fixture = new Fixture();
        fixture.Configuration.SummaryTranslation.Prompt = prompt;
        Assert.IsNull(await fixture.Translate());
        Assert.AreEqual(0, fixture.Calls);
    }

    [TestMethod]
    public async Task ChangingSourcePromptProviderOrModelInvalidatesCache()
    {
        using var fixture = new Fixture();
        await fixture.Translate();
        fixture.Configuration.SummaryTranslation.Prompt += "\nDo not add explanations.";
        await fixture.Translate();
        fixture.Configuration.Providers[0].Model = "another-model";
        await fixture.Translate();
        fixture.Configuration.Providers[0].Endpoint = "http://another.test/v1";
        await fixture.Translate();
        fixture.Configuration.Providers[0].ApiKey = "changed-test-token";
        await fixture.Translate();
        await fixture.Translator.TryTranslateSummaryAsync(Request with { SourceText = Japanese + "！" }, CancellationToken.None);
        Assert.AreEqual(5, fixture.Calls);
    }

    [TestMethod]
    public async Task FileCacheSurvivesRestartAndDoesNotExpireOrIncreaseUsage()
    {
        MockedApplicationPaths paths;
        string file;
        using (var first = new Fixture())
        {
            paths = first.Paths;
            Assert.AreEqual("少年和伙伴们一起踏上冒险旅程。", await first.Translate());
            file = Directory.GetFiles(first.CacheDirectory, "*.txt").Single();
            Assert.AreEqual(64, Path.GetFileNameWithoutExtension(file).Length);
            Assert.AreEqual("少年和伙伴们一起踏上冒险旅程。", await File.ReadAllTextAsync(file));
            Assert.IsFalse(Directory.EnumerateFiles(first.CacheDirectory, "*.tmp").Any());
        }
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddYears(-2));
        using var restarted = new Fixture((_, _) => throw new AssertFailedException("A persisted cache hit must not call the model."), paths);
        Assert.AreEqual("少年和伙伴们一起踏上冒险旅程。", await restarted.Translator.TryTranslateSummaryAsync(
            Request with { SubjectId = 456 }, CancellationToken.None));
        Assert.AreEqual(0, restarted.Calls);
        Assert.AreEqual(1L, (await restarted.Statistics.GetAsync()).Rows.Single().Calls);
    }

    [TestMethod]
    public async Task FileCacheIsReusedAfterProviderRenameCredentialRotationAndPriceChange()
    {
        using var first = new Fixture();
        first.Configuration.Providers[0].ApiKey = "first-test-credential";
        await first.Translate();
        using var restarted = new Fixture(paths: first.Paths);
        var provider = restarted.Configuration.Providers[0];
        provider.Id = "new-id";
        provider.Name = "new-name";
        provider.ApiKey = "second-test-credential";
        provider.Pricing.Input = 2;
        restarted.Configuration.SummaryTranslation.ProviderId = provider.Id;
        Assert.AreEqual("少年和伙伴们一起踏上冒险旅程。", await restarted.Translate());
        Assert.AreEqual(0, restarted.Calls);
        Assert.AreEqual(1, Directory.GetFiles(restarted.CacheDirectory, "*.txt").Length);
    }

    [DataTestMethod]
    [DataRow("source")]
    [DataRow("prompt")]
    [DataRow("model")]
    [DataRow("endpoint")]
    [DataRow("context")]
    public async Task ChangedTranslationInputsDoNotReusePersistedResult(string change)
    {
        using var first = new Fixture();
        await first.Translate();
        using var restarted = new Fixture(paths: first.Paths);
        var request = Request;
        switch (change)
        {
            case "source": request = request with { SourceText = Japanese + "！" }; break;
            case "prompt": restarted.Configuration.SummaryTranslation.Prompt += "\nUse Traditional Chinese."; break;
            case "model": restarted.Configuration.Providers[0].Model = "new-model"; break;
            case "endpoint": restarted.Configuration.Providers[0].Endpoint = "http://other.test/v1"; break;
            case "context": request = request with { LocalizedTitle = "另一译名" }; break;
        }
        Assert.IsNotNull(await restarted.Translator.TryTranslateSummaryAsync(request, CancellationToken.None));
        Assert.AreEqual(1, restarted.Calls);
        Assert.AreEqual(2, Directory.GetFiles(restarted.CacheDirectory, "*.txt").Length);
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("```json\n{}\n```")]
    [DataRow("<think>Reasoning</think>")]
    public async Task UnusableFileCacheIsReplacedWithAValidTranslation(string invalid)
    {
        using var first = new Fixture();
        await first.Translate();
        var file = Directory.GetFiles(first.CacheDirectory, "*.txt").Single();
        await File.WriteAllTextAsync(file, invalid);
        using var restarted = new Fixture(paths: first.Paths);
        Assert.AreEqual("少年和伙伴们一起踏上冒险旅程。", await restarted.Translate());
        Assert.AreEqual(1, restarted.Calls);
        Assert.AreEqual("少年和伙伴们一起踏上冒险旅程。", await File.ReadAllTextAsync(file));
    }

    [TestMethod]
    public async Task OversizedFileCacheIsIgnored()
    {
        using var first = new Fixture();
        await first.Translate();
        var file = Directory.GetFiles(first.CacheDirectory, "*.txt").Single();
        await File.WriteAllTextAsync(file, new string('a', 129 * 1024));
        using var restarted = new Fixture(paths: first.Paths);
        Assert.IsNotNull(await restarted.Translate());
        Assert.AreEqual(1, restarted.Calls);
    }

    [TestMethod]
    public async Task FileCacheReadAndWriteFailureKeepsTranslatedResultAndMemoryCache()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.CacheDirectory)!);
        // A file in place of the cache directory deterministically blocks I/O on every platform.
        await File.WriteAllTextAsync(fixture.CacheDirectory, "blocked");
        Assert.AreEqual("少年和伙伴们一起踏上冒险旅程。", await fixture.Translate());
        Assert.AreEqual("少年和伙伴们一起踏上冒险旅程。", await fixture.Translate());
        Assert.AreEqual(1, fixture.Calls);
    }

    [TestMethod]
    public async Task ConcurrentRefreshesShareCallAndOneCancelledWaiterDoesNotCancelOthers()
    {
        var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new Fixture((_, _) => response.Task);
        using var cancelled = new CancellationTokenSource();
        var first = fixture.Translator.TryTranslateSummaryAsync(Request, cancelled.Token);
        var second = fixture.Translate();
        await cancelled.CancelAsync();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () => await first);
        response.SetResult(Fixture.Reply("共享的翻译结果。"));
        Assert.AreEqual("共享的翻译结果。", await second);
        Assert.AreEqual("共享的翻译结果。", await fixture.Translate());
        Assert.AreEqual(1, fixture.Calls);
    }

    [TestMethod]
    public async Task ModelCallsAreLimitedToTwoAcrossDifferentSummaries()
    {
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new Fixture((_, _) => release.Task);
        var calls = Enumerable.Range(0, 5).Select(id => fixture.Translator.TryTranslateSummaryAsync(
            Request with { SourceText = Japanese + id }, CancellationToken.None)).ToArray();
        Assert.AreEqual(2, fixture.Calls);
        release.SetResult(Fixture.Reply("翻译结果。"));
        await Task.WhenAll(calls);
        Assert.AreEqual(5, fixture.Calls);
    }

    [DataTestMethod]
    [DataRow("{}")]
    [DataRow("not json")]
    [DataRow("{\"choices\":[{\"message\":{\"content\":\"```json\\n{}\\n```\"}}]}")]
    [DataRow("{\"choices\":[{\"message\":{\"content\":\"部分翻译\"},\"finish_reason\":\"length\"}]}")]
    [DataRow("{\"content\":[{\"type\":\"text\",\"text\":\"部分翻译\"}],\"stop_reason\":\"max_tokens\"}", "Anthropic")]
    [DataRow("{\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"部分翻译\"}]}],\"status\":\"incomplete\"}", "Responses")]
    public async Task InvalidOrTruncatedResultsFallBackAndBrieflySuppressRetries(string reply, string format = "OpenAI")
    {
        using var fixture = new Fixture((_, _) => Task.FromResult(reply));
        fixture.Configuration.Providers[0].Format = format;
        Assert.IsNull(await fixture.Translate());
        Assert.IsNull(await fixture.Translate());
        Assert.AreEqual(1, fixture.Calls);
        Assert.IsFalse(Directory.Exists(fixture.CacheDirectory), "Unusable model output must not be persisted.");
    }

    [TestMethod]
    public async Task TransportFailureAndTimeoutFallBackButCallerCancellationPropagates()
    {
        foreach (var error in new Exception[] { new HttpRequestException("upstream failure"), new TaskCanceledException("timeout") })
        {
            using var fixture = new Fixture((_, _) => Task.FromException<string>(error));
            Assert.IsNull(await fixture.Translate());
        }
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        using var unused = new Fixture();
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => unused.Translator.TryTranslateSummaryAsync(Request, cancelled.Token));
        Assert.AreEqual(0, unused.Calls);
    }

    [TestMethod]
    public async Task MetadataProvidersApplyTranslationAndPreserveSourceOnFallback()
    {
        var api = ServiceLocator.GetService<BangumiApi>();
        var subject = (await api.GetSubject(23119, CancellationToken.None))!;
        var source = subject.Summary;
        foreach (var translated in new[] { "已翻译简介。", null })
        {
            var translator = new StubTranslator(translated);
            var movie = await new MovieProvider(api, ServiceLocator.GetService<Logger<MovieProvider>>(), translator)
                .GetMetadata(new MovieInfo { ProviderIds = new() { [Constants.ProviderName] = "23119" } }, CancellationToken.None);
            Assert.AreEqual(translated ?? source, movie.Item.Overview);
            var series = await new SeriesProvider(api, ServiceLocator.GetService<Logger<SeriesProvider>>(), translator)
                .GetMetadata(new SeriesInfo { ProviderIds = new() { [Constants.ProviderName] = "23119" } }, CancellationToken.None);
            Assert.AreEqual(translated ?? source, series.Item.Overview);
            var album = await new AlbumProvider(api, ServiceLocator.GetService<Logger<AlbumProvider>>(), translator)
                .GetMetadata(new AlbumInfo { ProviderIds = new() { [Constants.ProviderName] = "23119" } }, CancellationToken.None);
            Assert.AreEqual(translated ?? source, album.Item.Overview);
            var book = await new BookProvider(api, translator)
                .GetMetadata(new BookInfo { ProviderIds = new() { [Constants.ProviderName] = "23119" } }, CancellationToken.None);
            Assert.AreEqual(translated ?? source, book.Item.Overview);
            var season = await new SeasonProvider(api, ServiceLocator.GetService<Logger<SeasonProvider>>(),
                    ServiceLocator.GetService<ILibraryManager>(), translator)
                .GetMetadata(new SeasonInfo { ProviderIds = new() { [Constants.ProviderName] = "23119" }, IndexNumber = 1 }, CancellationToken.None);
            Assert.AreEqual(translated ?? source, season.Item.Overview);
            Assert.AreEqual(subject.OriginalName, translator.LastRequest!.OriginalTitle);
            Assert.AreEqual(subject.ChineseName, translator.LastRequest.LocalizedTitle);
            Assert.AreEqual(source, translator.LastRequest.SourceText);
            Assert.AreEqual(source, subject.Summary, "Raw Bangumi metadata must not be mutated.");
        }
    }

    private sealed class StubTranslator(string? result) : IAiSummaryTranslator
    {
        public SummaryTranslationRequest? LastRequest { get; private set; }
        public Task<string?> TryTranslateSummaryAsync(SummaryTranslationRequest request, CancellationToken token)
        {
            LastRequest = request;
            return Task.FromResult(result);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http;
        public MockedApplicationPaths Paths { get; }
        public AiConfiguration Configuration { get; } = new();
        public AiStatisticsStore Statistics { get; }
        public string CacheDirectory => Path.Join(Paths.DataPath, "bangumi", "ai-summary-cache", "v1");
        public AiSummaryTranslator Translator { get; }
        public int Calls;
        public string? LastPrompt { get; private set; }

        public Fixture(Func<HttpRequestMessage, CancellationToken, Task<string>>? send = null, MockedApplicationPaths? paths = null)
        {
            Paths = paths ?? new MockedApplicationPaths(FakePath.Create("summary-" + Guid.NewGuid()));
            Statistics = new AiStatisticsStore(Paths);
            Configuration.Providers.Add(new AiProviderConfiguration { Id = "provider", Endpoint = "http://model.test/v1", Model = "test-model" });
            Configuration.SummaryTranslation.Enabled = true;
            Configuration.SummaryTranslation.ProviderId = "provider";
            _http = new HttpClient(new Handler(async (request, token) =>
            {
                Interlocked.Increment(ref Calls);
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                LastPrompt = Configuration.Providers[0].Format == "Responses"
                    ? body.RootElement.GetProperty("input").GetString()
                    : body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(send == null ? Reply("少年和伙伴们一起踏上冒险旅程。") : await send(request, token))
                };
            }));
            Translator = new AiSummaryTranslator(new AiProviderClient(_http, Statistics, NullLogger<AiProviderClient>.Instance),
                NullLogger<AiSummaryTranslator>.Instance, Paths, () => Configuration);
        }

        public Task<string?> Translate() => Translator.TryTranslateSummaryAsync(Request, CancellationToken.None);
        public static string Reply(string text) => JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = text } } } });
        public void Dispose()
        {
            Translator.Dispose();
            _http.Dispose();
            Statistics.Dispose();
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
