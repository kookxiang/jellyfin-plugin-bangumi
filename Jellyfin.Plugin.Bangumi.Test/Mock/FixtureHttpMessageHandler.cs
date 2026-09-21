using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Bangumi.Test.Mock;

/// <summary>Replays embedded responses only; there is deliberately no network fallback.</summary>
internal sealed class FixtureHttpMessageHandler(Action<string> onUnmatchedRequest) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var uri = request.RequestUri!;
        var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var key = $"{request.Method} {uri.PathAndQuery}\n{body}";
        if (uri.GetLeftPart(UriPartial.Authority) != "https://api.bgm.tv")
            throw Unmatched($"Unexpected HTTP destination: {uri}");

        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        var resource = $"Jellyfin.Plugin.Bangumi.Test.Fixtures.Api.{name}.json";
        await using var stream = typeof(FixtureHttpMessageHandler).Assembly.GetManifestResourceStream(resource);
        if (stream == null)
            throw Unmatched($"Missing Bangumi API fixture {name}.json for {key}");

        var fixture = (await JsonSerializer.DeserializeAsync<Fixture>(stream, cancellationToken: cancellationToken))!;
        if (fixture.Request != key)
            throw Unmatched($"Bangumi API fixture request mismatch: {key}");

        var response = new HttpResponseMessage((HttpStatusCode)fixture.Status)
        {
            Content = new StringContent(fixture.Body, Encoding.UTF8, "application/json"),
            RequestMessage = request
        };
        if (fixture.Location != null)
            response.Headers.Location = new Uri(fixture.Location);
        return response;
    }

    private InvalidOperationException Unmatched(string message)
    {
        onUnmatchedRequest(message);
        return new InvalidOperationException(message);
    }

    private sealed record Fixture(string Request, int Status, string Body, string? Location);
}
