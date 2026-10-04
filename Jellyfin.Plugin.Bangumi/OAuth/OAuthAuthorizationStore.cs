using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Jellyfin.Plugin.Bangumi.OAuth;

public sealed class OAuthAuthorizationStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, OAuthAuthorization> _states = new();
    private readonly Func<DateTimeOffset> _utcNow;

    public OAuthAuthorizationStore() : this(() => DateTimeOffset.UtcNow)
    {
    }

    internal OAuthAuthorizationStore(Func<DateTimeOffset> utcNow)
    {
        _utcNow = utcNow;
    }

    public OAuthAuthorization Create(Guid userId, string callbackUrl, string serverUrl)
    {
        RemoveExpired();
        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var authorization = new OAuthAuthorization(state, userId, callbackUrl, serverUrl, _utcNow().Add(Lifetime));
        _states[state] = authorization;
        return authorization;
    }

    public OAuthAuthorization? Consume(string state)
    {
        if (!_states.TryRemove(state, out var authorization) || authorization.ExpiresAt <= _utcNow())
            return null;
        return authorization;
    }

    private void RemoveExpired()
    {
        foreach (var entry in _states)
            if (entry.Value.ExpiresAt <= _utcNow())
                _states.TryRemove(entry.Key, out _);
    }
}

public sealed record OAuthAuthorization(
    string State,
    Guid UserId,
    string CallbackUrl,
    string ServerUrl,
    DateTimeOffset ExpiresAt);
