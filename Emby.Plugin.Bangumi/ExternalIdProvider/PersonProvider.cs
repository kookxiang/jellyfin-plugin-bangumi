using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Bangumi.ExternalIdProvider;

public class PersonProvider(BangumiApi api)
    : IRemoteMetadataProvider<Person, PersonLookupInfo>, IHasOrder, IHasSupportedExternalIdentifiers
{
    public int Order => -5;

    public string Name => Constants.ProviderName;
    
    public string[] GetSupportedExternalIdentifiers() => [Constants.ProviderName];

    public async Task<MetadataResult<Person>> GetMetadata(PersonLookupInfo info, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new MetadataResult<Person> { ResultLanguage = Constants.Language };
        if (!int.TryParse(info.ProviderIds?.GetValueOrDefault(Constants.ProviderName), out var personId))
            return result;
        var person = await api.GetPerson(personId, cancellationToken);
        if (person == null)
            return result;
        result.HasMetadata = true;
        result.Item = new Person
        {
            Name = person.Name,
            Overview = person.Summary,
            PremiereDate = person.Birthdate,
            ProductionYear = person.BirthYear
        };
        result.Item.ProviderIds.Add(Constants.ProviderName, $"{person.Id}");
        return result;
    }

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(PersonLookupInfo searchInfo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var results = new List<RemoteSearchResult>();

        if (!int.TryParse(searchInfo.ProviderIds?.GetValueOrDefault(Constants.ProviderName), out var id))
        {
            var persons = await api.SearchPerson(searchInfo.Name, cancellationToken);

            results.AddRange((persons ?? []).Select(item => new RemoteSearchResult
            {
                Name = item.Name,
                SearchProviderName = item.Name,
                ImageUrl = item.DefaultImage,
                Overview = item.ShortSummary,
                ProviderIds = { { Constants.ProviderName, item.Id.ToString() } }
            }));

            return results;
        }

        var person = await api.GetPerson(id, cancellationToken);
        if (person == null)
            return results;

        var result = new RemoteSearchResult
        {
            Name = person.Name,
            SearchProviderName = person.Name,
            ImageUrl = person.DefaultImage,
            Overview = person.Summary,
            PremiereDate = person.Birthdate
        };
        result.ProviderIds.Add(Constants.ProviderName, id.ToString());
        results.Add(result);
        return results;
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
