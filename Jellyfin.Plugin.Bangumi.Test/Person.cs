using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Providers;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test
{
    [TestClass]
    public class Person
    {
        private readonly PersonProvider _provider = new(new TestApplicationPaths(),
            new NullLogger<PersonProvider>());

        private readonly CancellationToken _token = new();

        [TestMethod]
        public async Task GetById()
        {
            var result = await _provider.GetMetadata(new PersonLookupInfo
            {
                ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "5847" } }
            }, _token);
            Assert.IsNotNull(result.Item, "person info should not be null");
            Assert.AreEqual("茅野愛衣", result.Item.Name, "should return correct name");
            Assert.IsNotNull(result.Item.ProviderIds[Constants.ProviderName], "should have plugin provider id");
        }

        [TestMethod]
        public async Task ImageProvider()
        {
            var imgList = await new PersonImageProvider().GetImages(new MediaBrowser.Controller.Entities.TV.Episode
            {
                ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "5847" } }
            }, _token);
            Assert.IsTrue(imgList.Any(), "should return at least one image");
        }
    }
}