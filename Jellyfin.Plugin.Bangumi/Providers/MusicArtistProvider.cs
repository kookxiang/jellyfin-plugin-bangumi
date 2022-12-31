using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Bangumi.Providers;

public class MusicArtistProvider : IRemoteMetadataProvider<MusicArtist, ArtistInfo>, IHasOrder
{
    private readonly BangumiApi _api;

    public MusicArtistProvider(BangumiApi api)
    {
        _api = api;
    }

    public int Order => -5;

    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<MusicArtist>> GetMetadata(ArtistInfo info, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = new MetadataResult<MusicArtist> { ResultLanguage = Constants.Language };
        if (!int.TryParse(info.ProviderIds?.GetValueOrDefault(Constants.ProviderName), out var personId))
            return result;
        var person = await _api.GetPerson(personId, token);
        if (person == null)
            return result;
        result.HasMetadata = true;
        result.Item = new MusicArtist
        {
            Name = person.Name,
            Overview = person.Summary,
            PremiereDate = person.Birthdate,
            ProductionYear = person.BirthYear
        };
        result.Item.ProviderIds.Add(Constants.ProviderName, $"{person.Id}");
        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(ArtistInfo searchInfo, CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken token)
    {
        return await _api.GetHttpClient().GetAsync(url, token).ConfigureAwait(false);
    }
}