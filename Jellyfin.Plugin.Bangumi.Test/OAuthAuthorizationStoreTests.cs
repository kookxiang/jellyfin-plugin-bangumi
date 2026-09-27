using System;
using Jellyfin.Plugin.Bangumi.OAuth;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class OAuthAuthorizationStoreTests
{
    [TestMethod]
    public void Consume_IsOneTimeAndKeepsBoundUser()
    {
        var store = new OAuthAuthorizationStore();
        var userId = Guid.NewGuid();
        var authorization = store.Create(userId, "https://jellyfin.example/Plugins/Bangumi/OAuth", "https://jellyfin.example");

        var consumed = store.Consume(authorization.State);

        Assert.IsNotNull(consumed);
        Assert.AreEqual(userId, consumed.UserId);
        Assert.IsNull(store.Consume(authorization.State));
        Assert.IsNull(store.Consume("unknown-state"));
    }

    [TestMethod]
    public void Create_UsesDifferentCryptographicStates()
    {
        var store = new OAuthAuthorizationStore();
        var first = store.Create(Guid.NewGuid(), "https://example.test/callback", "https://example.test");
        var second = store.Create(Guid.NewGuid(), "https://example.test/callback", "https://example.test");

        Assert.AreNotEqual(first.State, second.State);
        Assert.IsTrue(first.State.Length >= 40);
    }

    [TestMethod]
    public void Consume_RejectsExpiredState()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new OAuthAuthorizationStore(() => now);
        var authorization = store.Create(Guid.NewGuid(), "https://example.test/callback", "https://example.test");

        now = now.AddMinutes(11);

        Assert.IsNull(store.Consume(authorization.State));
    }
}
