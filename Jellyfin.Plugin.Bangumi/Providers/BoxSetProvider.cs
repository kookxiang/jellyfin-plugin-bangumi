using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Utils;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Bangumi.Providers;

public class BoxSetProvider(BangumiApi api) : IRemoteMetadataProvider<BoxSet, BoxSetInfo>, IHasOrder
{
    // FIXME: Bangumi 没有合集类型，优先使用其他规范的 BoxSetProvider
    public int Order => 100;

    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<BoxSet>> GetMetadata(BoxSetInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<BoxSet> { ResultLanguage = Constants.Language };

        if (!int.TryParse(info.ProviderIds.GetOrDefault(Constants.ProviderName), out var subjectId))
            return result;

        var subject = await api.GetSubject(subjectId, cancellationToken);
        if (subject == null)
            return result;

        result.Item = new BoxSet();
        result.HasMetadata = true;

        result.Item.ProviderIds.Add(Constants.ProviderName, subject.Id.ToString());
        result.Item.CommunityRating = subject.Rating?.Score;
        result.Item.Name = $"{subject.Name}（系列）";
        result.Item.Overview = string.IsNullOrEmpty(subject.Summary) ? null : subject.Summary;
        result.Item.Tags = subject.PopularTags.ToArray();

        return result;
    }

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BoxSetInfo searchInfo, CancellationToken cancellationToken)
    {
        var results = new List<RemoteSearchResult>();

        var series = await api.SearchSubject(searchInfo.Name, null, cancellationToken);
        foreach (var item in series)
        {
            var itemId = $"{item.Id}";
            var imageUrl = ImageUrlNormalizer.Normalize(item.DefaultImage);
            if (string.IsNullOrEmpty(imageUrl))
                imageUrl = await api.GetSubjectImage(item.Id, cancellationToken);
            var result = new RemoteSearchResult
            {
                Name = item.Name,
                SearchProviderName = item.OriginalName,
                ImageUrl = imageUrl,
                Overview = item.Summary
            };
            if (DateTime.TryParse(item.AirDate, out var airDate))
                result.PremiereDate = airDate;
            if (item.ProductionYear?.Length == 4)
                result.ProductionYear = int.Parse(item.ProductionYear);

            result.SetProviderId(Constants.ProviderName, itemId);
            results.Add(result);
        }

        return results;
    }

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        using var httpClient = api.GetHttpClient();
        return await httpClient.GetAsync(url, cancellationToken);
    }
}
