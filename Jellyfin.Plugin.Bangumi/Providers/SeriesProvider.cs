using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bangumi.Providers
{
    public class SeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>, IHasOrder
    {
        private readonly ILogger<SeriesProvider> _log;
        private readonly IApplicationPaths _paths;

        public SeriesProvider(IApplicationPaths appPaths, ILogger<SeriesProvider> logger)
        {
            _log = logger;
            _paths = appPaths;
        }

        public int Order => -5;
        public string Name => Constants.ProviderName;

        public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var result = new MetadataResult<Series> { ResultLanguage = Constants.Language };

            var bangumiId = info.ProviderIds.GetOrDefault(Constants.ProviderName);
            if (string.IsNullOrEmpty(bangumiId))
            {
                _log.LogInformation("Searching {Name} in bgm.tv", info.Name);
                var searchResult = await Api.SearchSeries(info.Name, token);
                if (searchResult.Count > 0)
                    bangumiId = $"{searchResult[0].Id}";
            }

            if (string.IsNullOrEmpty(bangumiId))
                return result;

            var subject = await Api.GetSeriesDetail(bangumiId, token);
            if (subject == null)
                return result;

            result.Item = new Series();
            result.HasMetadata = true;

            result.Item.ProviderIds.Add(Constants.ProviderName, bangumiId);
            if (!string.IsNullOrEmpty(subject.AirDate))
            {
                result.Item.PremiereDate = DateTime.Parse(subject.AirDate);
                result.Item.ProductionYear = DateTime.Parse(subject.AirDate).Year;
            }

            result.Item.AirTime = subject.AirDate;
            result.Item.AirDays = new[] { (DayOfWeek)(subject.AirWeekday % 7) };
            result.Item.CommunityRating = subject.Rating.Score;
            result.Item.Name = subject.Name;
            result.Item.Overview = subject.Summary;

            subject.StaffList?.ForEach(staff =>
            {
                string personType;
                if (staff.Jobs.Contains("导演"))
                    personType = PersonType.Director;
                else if (staff.Jobs.Contains("脚本"))
                    personType = PersonType.Writer;
                else
                    return;
                var person = new PersonInfo
                {
                    Name = staff.Name,
                    ImageUrl = staff.DefaultImage,
                    Type = personType
                };
                person.ProviderIds.Add(Constants.ProviderName, $"{staff.Id}");
                result.AddPerson(person);
            });

            subject.Characters?.ForEach(character =>
            {
                if (character.Actors?[0] == null) return;
                var person = new PersonInfo
                {
                    Name = character.Actors[0].Name,
                    Role = character.Name,
                    ImageUrl = character.Actors[0].DefaultImage,
                    Type = PersonType.Actor
                };
                person.ProviderIds.Add(Constants.ProviderName, $"{character.Actors[0].Id}");
                result.AddPerson(person);
            });

            return result;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var results = new Dictionary<string, RemoteSearchResult>();

            var id = searchInfo.ProviderIds.GetOrDefault(Constants.ProviderName);

            if (!string.IsNullOrEmpty(id))
            {
                var subject = await Api.GetSeriesBasic(id, token);
                var result = new RemoteSearchResult
                {
                    Name = subject.Name,
                    SearchProviderName = subject.OriginalName,
                    ImageUrl = subject.DefaultImage,
                    Overview = subject.Summary
                };

                if (!string.IsNullOrEmpty(subject.AirDate))
                {
                    result.PremiereDate = DateTime.Parse(subject.AirDate);
                    result.ProductionYear = DateTime.Parse(subject.AirDate).Year;
                }

                result.SetProviderId(Constants.ProviderName, id);
                results.Add(id, result);
            }
            else if (!string.IsNullOrEmpty(searchInfo.Name))
            {
                var series = await Api.SearchSeries(searchInfo.Name, token);
                foreach (var item in series)
                {
                    var itemId = $"{item.Id}";
                    var result = new RemoteSearchResult
                    {
                        Name = item.Name,
                        SearchProviderName = item.OriginalName,
                        ImageUrl = item.DefaultImage,
                        Overview = item.Summary
                    };
                    result.SetProviderId(Constants.ProviderName, itemId);
                    results.Add(itemId, result);
                }
            }

            return results.Values;
        }

        public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken token)
        {
            var httpClient = Plugin.Instance.GetHttpClient();
            return await httpClient.GetAsync(url, token).ConfigureAwait(false);
        }
    }
}