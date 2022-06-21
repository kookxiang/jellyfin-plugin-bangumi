using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.Bangumi.OAuth;

public class OAuthStore
{
    private readonly IApplicationPaths _applicationPaths;

    private readonly Dictionary<string, OAuthUser> _users = new();

    public OAuthStore(IApplicationPaths applicationPaths)
    {
        _applicationPaths = applicationPaths;

        if (File.Exists(StorePath))
            _users = JsonSerializer.Deserialize<Dictionary<string, OAuthUser>>(File.ReadAllText(StorePath))!;
    }

    private string StorePath => Path.Join(_applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.Bangumi.OAuth.dat");

    public void Save()
    {
        File.WriteAllText(StorePath, JsonSerializer.Serialize(_users));
    }

    public bool Contains(string userId)
    {
        return _users.ContainsKey(userId);
    }

    public bool Contains(Guid guid)
    {
        return Contains(guid.ToString("N"));
    }

    public OAuthUser? Get(string userId)
    {
        return _users.ContainsKey(userId) ? _users[userId] : null;
    }

    public OAuthUser? Get(Guid guid)
    {
        return Get(guid.ToString("N"));
    }

    public OAuthUser? GetAvailable()
    {
        try
        {
            return _users.First(user => !user.Value.Expired).Value;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Set(string userId, OAuthUser oAuthResult)
    {
        _users[userId] = oAuthResult;
    }

    public void Set(Guid guid, OAuthUser oAuthResult)
    {
        Set(guid.ToString("N"), oAuthResult);
    }


    public void Delete(string userId)
    {
        _users.Remove(userId);
    }

    public void Delete(Guid guid)
    {
        Delete(guid.ToString("N"));
    }

    protected internal Dictionary<string, OAuthUser> GetUsers()
    {
        return _users;
    }
}