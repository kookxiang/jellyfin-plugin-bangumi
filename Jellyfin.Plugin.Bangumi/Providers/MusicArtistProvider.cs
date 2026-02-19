using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
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

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(ArtistInfo searchInfo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var results = new List<RemoteSearchResult>();

        if (int.TryParse(searchInfo.ProviderIds.GetOrDefault(Constants.ProviderName), out var id))
        {
            var person = await api.GetPerson(id, cancellationToken);
            if (person == null)
                return results;
            var result = new RemoteSearchResult
            {
                Name = person.Name,
                ImageUrl = person.DefaultImage,
                Overview = person.Summary
            };
            result.SetProviderId(Constants.ProviderName, id.ToString());
            results.Add(result);
        }
        else if (!string.IsNullOrEmpty(searchInfo.Name))
        {
            var persons = await api.SearchPerson(searchInfo.Name, cancellationToken);
            foreach (var item in persons ?? [])
            {
                var result = new RemoteSearchResult
                {
                    Name = item.Name,
                    ImageUrl = item.DefaultImage,
                    Overview = item.Career?.Any() == true ? string.Join(", ", item.Career) : null
                };
                result.SetProviderId(Constants.ProviderName, item.Id.ToString());
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
