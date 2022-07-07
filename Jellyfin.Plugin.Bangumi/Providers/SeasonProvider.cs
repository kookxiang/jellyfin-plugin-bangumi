using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bangumi.Providers;

public class SeasonProvider : IRemoteMetadataProvider<Season, SeasonInfo>, IHasOrder
{
    private readonly BangumiApi _api;
    private readonly ILogger<SeasonProvider> _log;
    private readonly Plugin _plugin;

    public SeasonProvider(Plugin plugin, BangumiApi api, ILogger<SeasonProvider> log)
    {
        _plugin = plugin;
        _api = api;
        _log = log;
    }

    public int Order => -5;
    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<Season>> GetMetadata(SeasonInfo info, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = new MetadataResult<Season> { ResultLanguage = Constants.Language };

        if (info.Path == null || !new DirectoryInfo(info.Path).Name.StartsWith("Season"))
            return result;

        var subjectId = info.ProviderIds.GetOrDefault(Constants.ProviderName);
        if (string.IsNullOrEmpty(subjectId) && info.IndexNumber == 1)
            subjectId = info.SeriesProviderIds.GetOrDefault(Constants.ProviderName);
        if (string.IsNullOrEmpty(subjectId))
            return result;

        var subject = await _api.GetSubject(subjectId, token);
        if (subject == null)
            return result;

        result.Item = new Season();
        result.HasMetadata = true;

        result.Item.ProviderIds.Add(Constants.ProviderName, subjectId);
        result.Item.CommunityRating = subject.Rating?.Score;
        result.Item.Name = subject.GetName(_plugin.Configuration);
        result.Item.OriginalTitle = subject.OriginalName;
        result.Item.Overview = subject.Summary;
        result.Item.Tags = subject.PopularTags;
        if (DateTime.TryParse(subject.AirDate, out var airDate))
            result.Item.PremiereDate = airDate;
        if (subject.ProductionYear?.Length == 4)
            result.Item.ProductionYear = int.Parse(subject.ProductionYear);

        (await _api.GetSubjectPeople(subjectId, token)).ForEach(result.AddPerson);
        (await _api.GetSubjectCharacters(subjectId, token)).ForEach(result.AddPerson);

        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeasonInfo info, CancellationToken token)
    {
        return Task.FromResult(Enumerable.Empty<RemoteSearchResult>());
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken token)
    {
        return _plugin.GetHttpClient().GetAsync(url, token);
    }
}