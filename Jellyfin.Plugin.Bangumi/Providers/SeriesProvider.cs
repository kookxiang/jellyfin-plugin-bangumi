using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Jellyfin.Plugin.Bangumi.Parser.AnitomyParser;

namespace Jellyfin.Plugin.Bangumi.Providers;

public class SeriesProvider(BangumiApi api, Logger<SeriesProvider> log)
    : IRemoteMetadataProvider<Series, SeriesInfo>, IHasOrder
{
    private static PluginConfiguration Configuration => Plugin.Instance!.Configuration;

    public int Order => -5;

    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var baseName = Path.GetFileName(info.Path);
        var result = new MetadataResult<Series> { ResultLanguage = info.MetadataLanguage ?? Constants.Language };
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
            // Determine search order based on configuration
            var firstSearch = Configuration.UseOriginalTitleFirst ? info.OriginalTitle : info.Name;
            var secondSearch = Configuration.UseOriginalTitleFirst ? info.Name : info.OriginalTitle;

            // First search attempt
            if (firstSearch != null)
            {
                log.Info("Searching {Name} in bgm.tv", firstSearch);
                var searchResult = await api.SearchSubject(firstSearch, cancellationToken);
                if (info.Year != null)
                    searchResult = searchResult.Where(x => x.ProductionYear == null || x.ProductionYear == info.Year?.ToString());
                if (searchResult.Any())
                    subjectId = searchResult.First().Id;
            }

            // Second search attempt (if first failed and titles are different)
            if (subjectId == 0 && secondSearch != null && !string.Equals(firstSearch, secondSearch, StringComparison.Ordinal))
            {
                log.Info("Searching {Name} in bgm.tv", secondSearch);
                var searchResult = await api.SearchSubject(secondSearch, cancellationToken);
                if (info.Year != null)
                    searchResult = searchResult.Where(x => x.ProductionYear == null || x.ProductionYear == info.Year?.ToString());
                if (searchResult.Any())
                    subjectId = searchResult.First().Id;
            }
        }

        if (subjectId == 0 && Configuration.AlwaysGetTitleByAnitomySharp)
        {
            var anitomy = new Anitomy(baseName);
            var searchName = anitomy.ExtractAnimeTitle() ?? info.Name;
            log.Info("Searching {Name} in bgm.tv", searchName);
            // 不保证使用非原名或中文进行查询时返回正确结果
            var searchResult = await api.SearchSubject(searchName, cancellationToken);
            if (info.Year != null)
                searchResult = searchResult.Where(x => x.ProductionYear == null || x.ProductionYear == info.Year?.ToString());
            if (searchResult.Any())
                subjectId = searchResult.First().Id;
        }

        if (subjectId == 0)
            return result;

        var subject = await api.GetSubject(subjectId, cancellationToken);

        // return if subject still not found
        if (subject == null)
            return result;

        result.Item = new Series();
        result.HasMetadata = true;

        result.Item.ProviderIds.Add(Constants.ProviderName, subject.Id.ToString());
        result.Item.CommunityRating = subject.Rating?.Score;
        result.Item.Name = subject.Name;
        result.Item.OriginalTitle = subject.OriginalName;
        result.Item.Overview = string.IsNullOrEmpty(subject.Summary) ? null : subject.Summary;
        result.Item.Tags = subject.PopularTags.ToArray();
        result.Item.Genres = subject.GenreTags.ToArray();
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

        try
        {
            (await api.GetSubjectPersonInfos(subject.Id, cancellationToken)).ToList().ForEach(result.AddPerson);
        }
        catch (Exception ex)
        {
            log.Error("Failed to get person infos for subject {0} : {1}", subject.Id, ex);
        }

        try
        {
            (await api.GetSubjectCharacters(subject.Id, cancellationToken)).ToList().ForEach(result.AddPerson);
        }
        catch (Exception ex)
        {
            log.Error("Failed to get person infos for subject {0} : {1}", subject.Id, ex);
        }

        return result;
    }

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var results = new List<RemoteSearchResult>();

        if (int.TryParse(searchInfo.ProviderIds.GetOrDefault(Constants.ProviderName), out var id))
        {
            var subject = await api.GetSubject(id, cancellationToken);
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
            var series = await api.SearchSubject(searchInfo.Name, cancellationToken);
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

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        using var httpClient = api.GetHttpClient();
        return await httpClient.GetAsync(url, cancellationToken);
    }
}
