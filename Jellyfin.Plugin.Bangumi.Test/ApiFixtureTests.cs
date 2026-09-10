using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Test.Mock;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class ApiFixtureTests
{
    [TestMethod]
    public async Task FixtureReturnsIndependentResponses()
    {
        using var client = new HttpClient(new FixtureHttpMessageHandler(Assert.Fail));
        using var first = await client.GetAsync("https://api.bgm.tv/v0/episodes/1143188");
        var content = await first.Content.ReadAsStringAsync();
        first.Dispose();
        using var second = await client.GetAsync("https://api.bgm.tv/v0/episodes/1143188");
        Assert.AreEqual(content, await second.Content.ReadAsStringAsync());
        StringAssert.Contains(content, "1143188");
    }

    [DataTestMethod]
    [DataRow("GET", "https://api.bgm.tv/v0/episodes/999999999", "")]
    [DataRow("GET", "https://example.invalid/v0/episodes/1143188", "")]
    [DataRow("POST", "https://api.bgm.tv/v0/episodes/1143188", "{}")]
    [DataRow("GET", "https://api.bgm.tv/v0/episodes/1143188?unexpected=true", "")]
    public async Task UnmatchedRequestsFailWithoutNetwork(string method, string url, string body)
    {
        var unmatched = new List<string>();
        using var client = new HttpClient(new FixtureHttpMessageHandler(unmatched.Add));
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (body.Length > 0) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.SendAsync(request));
        Assert.AreEqual(1, unmatched.Count);
    }

    [TestMethod]
    public async Task CancelledRequestsDoNotReadFixtures()
    {
        using var client = new HttpClient(new FixtureHttpMessageHandler(Assert.Fail));
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() =>
            client.GetAsync("https://api.bgm.tv/v0/episodes/1143188", new CancellationToken(true)));
    }
}
