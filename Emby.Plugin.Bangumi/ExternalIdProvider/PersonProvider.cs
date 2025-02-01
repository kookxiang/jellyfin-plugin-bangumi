using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Bangumi.ExternalIdProvider;

public class PersonProvider(BangumiApi api)
    : IRemoteMetadataProvider<Person, PersonLookupInfo>, IHasOrder
{
    public int Order => -5;

    public string Name => Constants.ProviderName;

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

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(PersonLookupInfo searchInfo, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
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
