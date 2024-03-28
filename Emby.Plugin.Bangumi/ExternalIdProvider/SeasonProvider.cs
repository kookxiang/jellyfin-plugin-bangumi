﻿using System;
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

namespace Jellyfin.Plugin.Bangumi.Providers;

public class SeasonProvider : IRemoteMetadataProvider<Season, SeasonInfo>, IHasOrder
{
    private readonly BangumiApi _api;
    private readonly ILogger _log;

    public SeasonProvider(BangumiApi api, ILogger log)
    {
        _api = api;
        _log = log;
    }

    private static PluginConfiguration Configuration => Plugin.Instance!.Configuration;

    public int Order => -5;

    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<Season>> GetMetadata(SeasonInfo info, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = new MetadataResult<Season> { ResultLanguage = Constants.Language };

        if (!int.TryParse(info.ProviderIds.GetOrDefault(Constants.ProviderName), out var subjectId))
        {
            if (info.IndexNumber != 1 ||
            !int.TryParse(info.SeriesProviderIds.GetOrDefault(Constants.ProviderName), out subjectId))
                return result;
            _log.Info("Seacon GetMetadata from series id: {0}", subjectId);
        }
        else
        {
            _log.Info("Seacon GetMetadata from builtin id: {0}", subjectId);
        }

        var subject = await _api.GetSubject(subjectId, token);
        if (subject == null)
            return result;

        result.Item = new Season();
        result.HasMetadata = true;

        result.Item.ProviderIds.Add(Constants.ProviderName, subject.Id.ToString());
        result.Item.CommunityRating = subject.Rating?.Score;
        if (Configuration.UseBangumiSeasonTitle)
        {
            result.Item.Name = subject.Name;
            result.Item.OriginalTitle = subject.OriginalName;
        }

        result.Item.Overview = string.IsNullOrEmpty(subject.Summary) ? null : subject.Summary;
        result.Item.Tags = subject.PopularTags;

        if (DateTime.TryParse(subject.AirDate, out var airDate))
        {
            result.Item.PremiereDate = airDate;
            result.Item.ProductionYear = airDate.Year;
        }

        if (subject.ProductionYear?.Length == 4)
            result.Item.ProductionYear = int.Parse(subject.ProductionYear);

        if (subject.IsNSFW)
            result.Item.OfficialRating = "X";

        (await _api.GetSubjectPersonInfos(subject.Id, token)).ForEach(result.AddPerson);
        (await _api.GetSubjectCharacters(subject.Id, token)).ForEach(result.AddPerson);

        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeasonInfo info, CancellationToken token)
    {
        return Task.FromResult(Enumerable.Empty<RemoteSearchResult>());
    }

    public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken token)
    {
        return _api.GetHttpClient().GetResponse(new HttpRequestOptions
        {
            Url = url,
            CancellationToken = token
        });
    }
}