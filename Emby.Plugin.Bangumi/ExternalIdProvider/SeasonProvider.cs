using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Bangumi.ExternalIdProvider;

public class SeasonProvider(BangumiApi api, ILogger log)
    : IRemoteMetadataProvider<Season, SeasonInfo>, IHasOrder
{
    private static PluginConfiguration Configuration => Plugin.Instance!.Configuration;

    public int Order => -5;

    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<Season>> GetMetadata(SeasonInfo info, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new MetadataResult<Season> { ResultLanguage = Constants.Language };

        if (!int.TryParse(info.ProviderIds.GetOrDefault(Constants.ProviderName), out var subjectId))
        {
            if (info.IndexNumber != 1 ||
                !int.TryParse(info.SeriesProviderIds.GetOrDefault(Constants.ProviderName), out subjectId))
                return result;
            log.Info("Seacon GetMetadata from series id: {0}", subjectId);
        }
        else
        {
            log.Info("Seacon GetMetadata from builtin id: {0}", subjectId);
        }

        var subject = await api.GetSubject(subjectId, cancellationToken);
        if (subject == null)
            return result;

        result.Item = new Season();
        result.HasMetadata = true;

        result.Item.ProviderIds.Add(Constants.ProviderName, subject.Id.ToString());
        result.Item.CommunityRating = subject.Rating?.Score;
        if (Configuration.UseBangumiSeasonTitle)
        {
            result.Item.Name = subject.Name;
            result.Item.SortName = subject.Name;
            result.Item.OriginalTitle = subject.OriginalName;
        }

        result.Item.Overview = string.IsNullOrEmpty(subject.Summary) ? null : subject.Summary;
        result.Item.SetTags(subject.PopularTags);
        result.Item.SetGenres(subject.GenreTags);

        if (DateTime.TryParse(subject.AirDate, out var airDate))
        {
            result.Item.PremiereDate = airDate;
            result.Item.ProductionYear = airDate.Year;
        }

        if (subject.ProductionYear?.Length == 4)
            result.Item.ProductionYear = int.Parse(subject.ProductionYear);

        if (subject.IsNSFW)
            result.Item.OfficialRating = "X";

        (await api.GetSubjectPersonInfos(subject.Id, cancellationToken)).ToList().ForEach(result.AddPerson);
        (await api.GetSubjectCharacters(subject.Id, cancellationToken)).ToList().ForEach(result.AddPerson);

        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeasonInfo info, CancellationToken cancellationToken)
    {
        return Task.FromResult(Enumerable.Empty<RemoteSearchResult>());
    }

    public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return api.GetHttpClient().GetResponse(new HttpRequestOptions
        {
            Url = url,
            CancellationToken = cancellationToken
        });
    }
}
