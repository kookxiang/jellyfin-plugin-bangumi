using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Jellyfin.Plugin.Bangumi.AI;
using Jellyfin.Plugin.Bangumi.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Jellyfin.Plugin.Bangumi.Test.Mock;
using Jellyfin.Plugin.Bangumi.Test.Util;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class AiProviderTests
{
    [DataTestMethod]
    [DataRow("https://example.test", "OpenAI", "https://example.test/v1/chat/completions")]
    [DataRow("https://example.test/v1/", "Responses", "https://example.test/v1/responses")]
    [DataRow("http://localhost:8080/proxy/v1", "Anthropic", "http://localhost:8080/proxy/v1/messages")]
    [DataRow("https://example.test/custom/chat/completions", "OpenAI", "https://example.test/custom/chat/completions")]
    public void EndpointAcceptsBaseAndFullAddresses(string input, string format, string expected)
        => Assert.AreEqual(expected, AiProviderClient.BuildEndpoint(input, format).AbsoluteUri);

    [DataTestMethod]
    [DataRow("file:///tmp/model", "OpenAI")]
    [DataRow("https://user:password@example.test/v1", "OpenAI")]
    [DataRow("https://example.test/v1?key=test", "OpenAI")]
    [DataRow("https://example.test/v1/messages", "Responses")]
    [DataRow("https://example.test/v1", "Unknown")]
    public void InvalidEndpointsAreRejectedBeforeSending(string input, string format)
        => Assert.ThrowsException<ArgumentException>(() => AiProviderClient.BuildEndpoint(input, format));

    [DataTestMethod]
    [DataRow("OpenAI", "{\"choices\":[{\"message\":{\"content\":\"你好\"}}]}")]
    [DataRow("Responses", "{\"output\":[{\"type\":\"reasoning\",\"summary\":[]},{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"你好\"}]}]}")]
    [DataRow("Anthropic", "{\"content\":[{\"type\":\"thinking\",\"thinking\":\"hidden\"},{\"type\":\"text\",\"text\":\"你好\"}]}")]
    public async Task ProbeUsesSelectedProtocolAndReadsOnlyReplyText(string format, string reply)
    {
        using var handler = new ProbeHandler(async (request, token) =>
        {
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.IsFalse(request.Headers.Contains("x-api-key") && format != "Anthropic");
            if (format == "Anthropic")
            {
                Assert.IsTrue(request.Headers.Contains("anthropic-version"));
                Assert.IsTrue(request.Headers.Contains("x-api-key"));
                Assert.IsNull(request.Headers.Authorization);
            }
            else Assert.AreEqual("Bearer", request.Headers.Authorization?.Scheme);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.AreEqual("test-model", body.RootElement.GetProperty("model").GetString());
            if (format == "Responses")
            {
                Assert.AreEqual(AiProviderClient.TestMessage, body.RootElement.GetProperty("input").GetString());
                Assert.IsFalse(body.RootElement.GetProperty("store").GetBoolean());
            }
            else Assert.AreEqual(AiProviderClient.TestMessage, body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply) };
        });
        using var httpClient = new HttpClient(handler);
        using var statistics = CreateStore();
        var client = CreateClient(httpClient, statistics);
        Assert.AreEqual("你好", (await client.TestAsync(new AiProviderConfiguration { Model = "test-model", Format = format, ApiKey = "test-only" }, CancellationToken.None)).Text);
    }

    [TestMethod]
    public async Task ProbeFailureDoesNotExposeUpstreamBodyAndEmptyRepliesAreNotSuccess()
    {
        using var handler = new ProbeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("echoed-sensitive-value")
        }));
        using var httpClient = new HttpClient(handler);
        using var statistics = CreateStore();
        var error = await Assert.ThrowsExceptionAsync<HttpRequestException>(() => CreateClient(httpClient, statistics)
            .TestAsync(new AiProviderConfiguration { Model = "test-model" }, CancellationToken.None));
        Assert.IsFalse(error.Message.Contains("echoed-sensitive-value"));
        Assert.IsTrue(error.Message.Contains("401"));

        using var emptyHandler = new ProbeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }));
        using var emptyClient = new HttpClient(emptyHandler);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => CreateClient(emptyClient, statistics)
            .TestAsync(new AiProviderConfiguration { Model = "test-model" }, CancellationToken.None));
    }

    [TestMethod]
    public async Task ControllerReturnsProbeDiagnosticsWithoutLosingThemToHostHttpErrors()
    {
        using var handler = new ProbeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("echoed-sensitive-value")
        }));
        using var httpClient = new HttpClient(handler);
        using var statistics = CreateStore();
        var controller = new AiController(CreateClient(httpClient, statistics), statistics);
        var result = (OkObjectResult)await controller.Test(new AiProviderConfiguration { Model = "test-model" }, CancellationToken.None);
        var body = JsonSerializer.SerializeToElement(result.Value);
        Assert.IsFalse(body.GetProperty("Success").GetBoolean());
        StringAssert.Contains(body.GetProperty("Message").GetString(), "401");
        Assert.IsFalse(body.GetProperty("Message").GetString()!.Contains("echoed-sensitive-value"));
    }

    [TestMethod]
    public void AiConfigurationRoundTripsThroughPluginXmlWithStableReferences()
    {
        var configuration = new AiConfiguration();
        configuration.Providers.Add(new AiProviderConfiguration { Id = "stable-id", Name = "Local", Endpoint = "http://localhost/v1", Model = "test", Format = "Responses", Pricing = new AiPricingConfiguration { Input = 1.25m, Output = 0m } });
        configuration.SummaryTranslation.ProviderId = "stable-id";
        configuration.SummaryTranslation.Prompt = "Custom {{source_text}}";
        var serializer = new XmlSerializer(typeof(AiConfiguration));
        using var writer = new StringWriter();
        serializer.Serialize(writer, configuration);
        using var reader = new StringReader(writer.ToString());
        var restored = (AiConfiguration)serializer.Deserialize(reader)!;
        Assert.AreEqual(restored.Providers[0].Id, restored.SummaryTranslation.ProviderId);
        Assert.AreEqual("Custom {{source_text}}", restored.SummaryTranslation.Prompt);
        Assert.AreEqual(1.25m, restored.Providers[0].Pricing.Input);
        Assert.AreEqual(0m, restored.Providers[0].Pricing.Output);
        Assert.IsNull(restored.Providers[0].Pricing.CachedInput);
        Assert.IsFalse(restored.SummaryTranslation.Enabled);
        Assert.IsFalse(restored.FallbackTitle.Enabled);
    }

    private static AiStatisticsStore CreateStore() => new(new MockedApplicationPaths(FakePath.Create("ai-" + Guid.NewGuid())));

    [DataTestMethod]
    [DataRow("<TargetLanguage>日本語</TargetLanguage><Prompt>Translate {{source_text}} to {{ target_language }}.</Prompt>")]
    [DataRow("<Prompt>Translate {{source_text}} to {{ target_language }}.</Prompt><TargetLanguage>日本語</TargetLanguage>")]
    public void RetiredLanguageMigratesFromXmlWithoutRemainingInSavedConfiguration(string fields)
    {
        var serializer = new XmlSerializer(typeof(AiFeatureConfiguration));
        using var reader = new StringReader("<AiFeatureConfiguration><Enabled>true</Enabled><ProviderId>stable-id</ProviderId>" + fields + "</AiFeatureConfiguration>");
        var feature = (AiFeatureConfiguration)serializer.Deserialize(reader)!;
        Assert.AreEqual("Translate {{source_text}} to 日本語.", feature.Prompt);
        Assert.IsTrue(feature.Enabled);
        Assert.AreEqual("stable-id", feature.ProviderId);
        using var writer = new StringWriter();
        serializer.Serialize(writer, feature);
        Assert.IsFalse(writer.ToString().Contains("TargetLanguage"));
        Assert.IsFalse(writer.ToString().Contains("target_language"));
        Assert.IsFalse(JsonSerializer.Serialize(feature).Contains("TargetLanguage"));
        using var restoredReader = new StringReader(writer.ToString());
        Assert.AreEqual(feature.Prompt, ((AiFeatureConfiguration)serializer.Deserialize(restoredReader)!).Prompt);
    }

    private static AiProviderClient CreateClient(HttpClient http, AiStatisticsStore statistics)
        => new(http, statistics, NullLogger<AiProviderClient>.Instance);

    private sealed class ProbeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
