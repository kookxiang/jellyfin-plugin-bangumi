using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Bangumi.Providers;

public class MusicArtistProvider(BangumiApi api)
    : IRemoteMetadataProvider<MusicArtist, ArtistInfo>, IHasOrder
{
    public int Order => -5;

    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<MusicArtist>> GetMetadata(ArtistInfo info, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new MetadataResult<MusicArtist> { ResultLanguage = Constants.Language };
        if (!int.TryParse(info.ProviderIds?.GetValueOrDefault(Constants.ProviderName), out var personId))
            return result;
        var person = await api.GetPerson(personId, cancellationToken);
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

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(ArtistInfo searchInfo, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        using var httpClient = api.GetHttpClient();
        return await httpClient.GetAsync(url, cancellationToken);
    }
}
