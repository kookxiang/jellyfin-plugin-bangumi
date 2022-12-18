﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bangumi.Providers;

public class AlbumProvider : IRemoteMetadataProvider<MusicAlbum, AlbumInfo>, IHasOrder
{
    private readonly BangumiApi _api;
    private readonly ILogger<AlbumProvider> _log;
    private readonly Plugin _plugin;

    public AlbumProvider(Plugin plugin, BangumiApi api, ILogger<AlbumProvider> log)
    {
        _plugin = plugin;
        _api = api;
        _log = log;
    }

    private PluginConfiguration Configuration => _plugin.Configuration;

    public int Order => -5;

    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<MusicAlbum>> GetMetadata(AlbumInfo info, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var baseName = Path.GetFileName(info.Path);
        var result = new MetadataResult<MusicAlbum> { ResultLanguage = Constants.Language };

        var subjectId = info.ProviderIds.GetOrDefault(Constants.ProviderName);

        if (string.IsNullOrEmpty(subjectId))
        {
            var searchName = info.Name;
            _log.LogInformation("Searching {Name} in bgm.tv", searchName);
            var searchResult = await _api.SearchSubject(searchName, SubjectType.Music, token);
            if (info.Year != null)
                searchResult = searchResult.FindAll(x => x.ProductionYear == null || x.ProductionYear == info.Year.ToString());
            if (searchResult.Count > 0)
                subjectId = $"{searchResult[0].Id}";
        }

        if (string.IsNullOrEmpty(subjectId) && info.OriginalTitle != null && !string.Equals(info.OriginalTitle, info.Name, StringComparison.Ordinal))
        {
            var searchName = info.OriginalTitle;
            _log.LogInformation("Searching {Name} in bgm.tv", searchName);
            var searchResult = await _api.SearchSubject(searchName, SubjectType.Music, token);
            if (info.Year != null)
                searchResult = searchResult.FindAll(x => x.ProductionYear == null || x.ProductionYear == info.Year.ToString());
            if (searchResult.Count > 0)
                subjectId = $"{searchResult[0].Id}";
        }

        if (string.IsNullOrEmpty(subjectId) && Configuration.AlwaysGetTitleByAnitomySharp)
        {
            var searchName = Anitomy.ExtractAnimeTitle(baseName) ?? info.Name;
            _log.LogInformation("Searching {Name} in bgm.tv", searchName);
            // 不保证使用非原名或中文进行查询时返回正确结果
            var searchResult = await _api.SearchSubject(searchName, SubjectType.Music, token);
            if (info.Year != null)
                searchResult = searchResult.FindAll(x => x.ProductionYear == null || x.ProductionYear == info.Year.ToString());
            if (searchResult.Count > 0)
                subjectId = $"{searchResult[0].Id}";
        }

        if (string.IsNullOrEmpty(subjectId))
            return result;

        var subject = await _api.GetSubject(subjectId, token);
        if (subject == null)
            return result;

        result.Item = new MusicAlbum();
        result.HasMetadata = true;

        result.Item.ProviderIds.Add(Constants.ProviderName, subjectId);
        result.Item.CommunityRating = subject.Rating?.Score;
        result.Item.Name = subject.GetName(Configuration);
        result.Item.OriginalTitle = subject.OriginalName;
        result.Item.Overview = string.IsNullOrEmpty(subject.Summary) ? null : subject.Summary;
        result.Item.Tags = subject.PopularTags;

        if (DateTime.TryParse(subject.AirDate, out var airDate))
        {
            result.Item.PremiereDate = airDate;
            result.Item.ProductionYear = airDate.Year;
        }

        if (subject.ProductionYear?.Length == 4)
            result.Item.ProductionYear = int.Parse(subject.ProductionYear);

        var persons = await _api.GetSubjectPersons(subjectId, token);
        result.Item.AlbumArtists = persons?.FindAll(person => person.Type == 3)?.ConvertAll(person => person.Name);

        return result;
    }

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(AlbumInfo searchInfo,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var results = new List<RemoteSearchResult>();

        var id = searchInfo.ProviderIds.GetOrDefault(Constants.ProviderName);

        if (!string.IsNullOrEmpty(id))
        {
            var subject = await _api.GetSubject(id, token);
            if (subject == null)
                return results;
            var result = new RemoteSearchResult
            {
                Name = subject.GetName(Configuration),
                SearchProviderName = subject.OriginalName,
                ImageUrl = subject.DefaultImage,
                Overview = subject.Summary
            };
            if (DateTime.TryParse(subject.AirDate, out var airDate))
                result.PremiereDate = airDate;
            if (subject.ProductionYear?.Length == 4)
                result.ProductionYear = int.Parse(subject.ProductionYear);
            result.SetProviderId(Constants.ProviderName, id);
            results.Add(result);
        }
        else if (!string.IsNullOrEmpty(searchInfo.Name))
        {
            var series = await _api.SearchSubject(searchInfo.Name, SubjectType.Music, token);
            series = Subject.SortBySimilarity(series, searchInfo.Name);
            foreach (var item in series)
            {
                var itemId = $"{item.Id}";
                var result = new RemoteSearchResult
                {
                    Name = item.GetName(Configuration),
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
        return await _api.GetHttpClient().GetAsync(url, token).ConfigureAwait(false);
    }
}