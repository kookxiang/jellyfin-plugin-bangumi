using System;
using System.Threading;
using Microsoft.Extensions.Caching.Memory;

namespace Jellyfin.Plugin.Bangumi;

public partial class BangumiApi
{
    private static readonly MemoryCache _requestedRefreshes = new(new MemoryCacheOptions { SizeLimit = 10000 });
    private static readonly AsyncLocal<bool> _freshMetadata = new();

    internal static bool IsFreshMetadataRefresh => _freshMetadata.Value;

    internal static void RequestFreshMetadata(string path) => _requestedRefreshes.Set(path, true,
        new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1) });

    internal static void CancelFreshMetadataRequest(string path) => _requestedRefreshes.Remove(path);

    // The Jellyfin queue runs in a different execution context from the tool request.
    // Consume the request when its metadata provider starts, without changing global settings.
    internal static IDisposable? BeginRequestedRefresh(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !_requestedRefreshes.TryGetValue<bool>(path, out var requested) || !requested)
            return null;
        _requestedRefreshes.Remove(path);
        return new FreshMetadataScope();
    }

    private sealed class FreshMetadataScope : IDisposable
    {
        private readonly bool _previous = _freshMetadata.Value;

        public FreshMetadataScope() => _freshMetadata.Value = true;

        public void Dispose() => _freshMetadata.Value = _previous;
    }
}
