using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class WebDevelopmentTestCases
{
#if DEBUG
    [DataTestMethod]
    [DataRow(null, null)]
    [DataRow("  ", null)]
    [DataRow("http://127.0.0.1:8765", "http://127.0.0.1:8765/")]
    [DataRow(" https://dev.example/vite/ ", "https://dev.example/vite/")]
    public void NormalizesDevelopmentServer(string? input, string? expected)
    {
        Assert.AreEqual(expected, Configuration.WebDevelopmentController.GetServerUrl(input));
    }

    [DataTestMethod]
    [DataRow("file:///tmp/web")]
    [DataRow("http://localhost:8765?x=1")]
    [DataRow("http://localhost:8765/#test")]
    [DataRow("http://user:password@localhost:8765")]
    [DataRow("localhost:8765")]
    public void RejectsInvalidDevelopmentServer(string input)
    {
        Assert.ThrowsException<ArgumentException>(() => Configuration.WebDevelopmentController.GetServerUrl(input));
    }
#else
    [TestMethod]
    public void ReleaseDoesNotContainDevelopmentEndpoint()
    {
        Assert.IsNull(typeof(Plugin).Assembly.GetType("Jellyfin.Plugin.Bangumi.Configuration.WebDevelopmentController"));
    }
#endif
}
