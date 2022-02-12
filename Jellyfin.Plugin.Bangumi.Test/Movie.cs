using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Providers;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test
{
    [TestClass]
    public class Movie
    {
        private readonly MovieProvider _provider = new(new TestApplicationPaths(),
            new NullLogger<MovieProvider>());

        private readonly CancellationToken _token = new();

        [TestMethod]
        public async Task GetByName()
        {
            var result = await _provider.GetMetadata(new MovieInfo
            {
                Name = "STEINS;GATE 負荷領域のデジャヴ"
            }, _token);
            AssertMovie(result);
        }

        [TestMethod]
        public async Task GetById()
        {
            var result = await _provider.GetMetadata(new MovieInfo
            {
                ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "23119" } }
            }, _token);
            AssertMovie(result);
        }

        [TestMethod]
        public async Task ActorOnlyInStaff()
        {
            Bangumi.Plugin.Instance!.Configuration.ActorOnlyInStaff = true;
            await GetById();
            Bangumi.Plugin.Instance!.Configuration.ActorOnlyInStaff = false;
        }

        [TestMethod]
        public async Task SearchByName()
        {
            var searchResults = await _provider.GetSearchResults(new MovieInfo
            {
                Name = "STEINS;GATE 負荷領域のデジャヴ"
            }, _token);
            Assert.IsTrue(searchResults.Any(x => x.ProviderIds[Constants.ProviderName].Equals("23119")), "should have correct search result");
        }

        [TestMethod]
        public async Task SearchById()
        {
            var searchResults = await _provider.GetSearchResults(new MovieInfo
            {
                ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "23119" } }
            }, _token);
            Assert.IsTrue(searchResults.Any(x => x.ProviderIds[Constants.ProviderName].Equals("23119")), "should have correct search result");
        }

        private static void AssertMovie(MetadataResult<MediaBrowser.Controller.Entities.Movies.Movie> result)
        {
            Assert.IsNotNull(result.Item, "series data should not be null");
            Assert.AreEqual("命运石之门 负荷领域的既视感", result.Item.Name, "should return correct series name");
            Assert.AreNotEqual("", result.Item.Overview, "should return series overview");
            Assert.AreEqual(DateTime.Parse("2013-04-20"), result.Item.PremiereDate, "should return correct premiere date");
            Assert.IsTrue(result.Item.CommunityRating is > 0 and <= 10, "should return rating info");
            Assert.IsNotNull(result.People.Find(x => x.IsType(PersonType.Actor)), "should have at least one actor");
            if (Bangumi.Plugin.Instance!.Configuration.ActorOnlyInStaff)
            {
                Assert.IsNull(result.People.Find(x => x.IsType(PersonType.Director)), "should have no director in result");
                Assert.IsNull(result.People.Find(x => x.IsType(PersonType.Writer)), "should have no writer in result");
            }
            else
            {
                Assert.IsNotNull(result.People.Find(x => x.IsType(PersonType.Director)), "should have at least one director");
                Assert.IsNotNull(result.People.Find(x => x.IsType(PersonType.Writer)), "should have at least one writer");
            }

            Assert.AreNotEqual("", result.People?[0].ImageUrl, "person should have image url");
            Assert.AreEqual("23119", result.Item.ProviderIds[Constants.ProviderName], "should have plugin provider id");
        }
    }
}