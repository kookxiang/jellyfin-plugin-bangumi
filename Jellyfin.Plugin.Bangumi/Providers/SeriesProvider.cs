using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bangumi.Providers;

public class SeriesProvider(BangumiApi api, ILogger<SeriesProvider> log)
    : IRemoteMetadataProvider<Series, SeriesInfo>, IHasOrder
{
    private static PluginConfiguration Configuration => Plugin.Instance!.Configuration;

    public int Order => -5;

    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var baseName = Path.GetFileName(info.Path);
        var result = new MetadataResult<Series>
        {
            ResultLanguage = Constants.Language
        };
        var localConfiguration = await LocalConfiguration.ForPath(info.Path);

        var bangumiId = baseName.GetAttributeValue("bangumi");
        if (!string.IsNullOrEmpty(bangumiId))
            info.SetProviderId(Constants.ProviderName, bangumiId);

        int subjectId;
        if (localConfiguration.Id != 0)
            subjectId = localConfiguration.Id;
        else
            _ = int.TryParse(info.ProviderIds.GetOrDefault(Constants.ProviderName), out subjectId);

        if (subjectId == 0)
        {
            var searchName = info.Name;
            log.LogInformation("Searching {Name} in bgm.tv", searchName);
            var searchResult = await api.SearchSubject(searchName, token);
            if (info.Year != null)
                searchResult = searchResult.FindAll(x =>
                    x.ProductionYear == null || x.ProductionYear == info.Year.ToString());
            if (searchResult.Count > 0)
                subjectId = searchResult[0].Id;
        }

        if (subjectId == 0 && info.OriginalTitle != null &&
            !string.Equals(info.OriginalTitle, info.Name, StringComparison.Ordinal))
        {
            var searchName = info.OriginalTitle;
            log.LogInformation("Searching {Name} in bgm.tv", searchName);
            var searchResult = await api.SearchSubject(searchName, token);
            if (info.Year != null)
                searchResult = searchResult.FindAll(x =>
                    x.ProductionYear == null || x.ProductionYear == info.Year.ToString());
            if (searchResult.Count > 0)
                subjectId = searchResult[0].Id;
        }

        if (subjectId == 0 && Configuration.AlwaysGetTitleByAnitomySharp)
        {
            var anitomy = new Anitomy(baseName);
            var searchName = anitomy.ExtractAnimeTitle() ?? info.Name;
            log.LogInformation("Searching {Name} in bgm.tv", searchName);
            // 不保证使用非原名或中文进行查询时返回正确结果
            var searchResult = await api.SearchSubject(searchName, token);
            if (info.Year != null)
                searchResult = searchResult.FindAll(x =>
                    x.ProductionYear == null || x.ProductionYear == info.Year.ToString());
            if (searchResult.Count > 0)
                subjectId = searchResult[0].Id;
        }

        if (subjectId == 0)
            return result;

        var subject = await api.GetSubject(subjectId, token);
        if (subject == null)
            return result;

        result.Item = new Series();
        result.HasMetadata = true;

        result.Item.ProviderIds.Add(Constants.ProviderName, subject.Id.ToString());
        result.Item.CommunityRating = subject.Rating?.Score;
        result.Item.Name = subject.Name;
        result.Item.OriginalTitle = subject.OriginalName;
        result.Item.Overview = string.IsNullOrEmpty(subject.Summary) ? null : subject.Summary;
        result.Item.Tags = subject.PopularTags;
        result.Item.HomePageUrl = subject.OfficialWebSite;
        result.Item.EndDate = subject.EndDate;

        if (DateTime.TryParse(subject.AirDate, out var airDate))
        {
            result.Item.AirTime = subject.AirDate;
            result.Item.AirDays = [airDate.DayOfWeek];
            result.Item.PremiereDate = airDate;
            result.Item.ProductionYear = airDate.Year;
        }

        if (subject.ProductionYear?.Length == 4)
            result.Item.ProductionYear = int.Parse(subject.ProductionYear);

        if (subject.IsNSFW)
            result.Item.OfficialRating = "X";

        (await api.GetSubjectPersonInfos(subject.Id, token)).ForEach(result.AddPerson);
        (await api.GetSubjectCharacters(subject.Id, token)).ForEach(result.AddPerson);

        return result;
    }

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var results = new List<RemoteSearchResult>();

        if (int.TryParse(searchInfo.ProviderIds.GetOrDefault(Constants.ProviderName), out var id))
        {
            var subject = await api.GetSubject(id, token);
            if (subject == null)
                return results;
            var result = new RemoteSearchResult
            {
                Name = subject.Name,
                SearchProviderName = subject.OriginalName,
                ImageUrl = subject.DefaultImage,
                Overview = subject.Summary
            };
            if (DateTime.TryParse(subject.AirDate, out var airDate))
                result.PremiereDate = airDate;
            if (subject.ProductionYear?.Length == 4)
                result.ProductionYear = int.Parse(subject.ProductionYear);
            result.SetProviderId(Constants.ProviderName, id.ToString());
            results.Add(result);
        }
        else if (!string.IsNullOrEmpty(searchInfo.Name))
        {
            var series = await api.SearchSubject(searchInfo.Name, token);
            series = Subject.SortBySimilarity(series, searchInfo.Name);
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
                if (DateTime.TryParse(item.AirDate, out var airDate))
                    result.PremiereDate = airDate;
                if (item.ProductionYear?.Length == 4)
                    result.ProductionYear = int.Parse(item.ProductionYear);
                if (result.ProductionYear != null && searchInfo.Year != null)
                    if (result.ProductionYear != searchInfo.Year)
                        continue;
                result.SetProviderId(Constants.ProviderName, itemId);
                results.Add(result);
            }
        }

        return results;
    }

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken token)
    {
        return await api.GetHttpClient().GetAsync(url, token).ConfigureAwait(false);
    }
}