using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Bangumi.Providers;

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

        // return if person still not found
        if (person == null)
            return result;

        result.HasMetadata = true;
        result.Item = new Person
        {
            Name = person.TranslatedName,
            OriginalTitle = person.Name,
            Overview = person.Summary,
            PremiereDate = person.Birthdate,
            ProductionYear = person.BirthYear,
            ProductionLocations = [person.BirthPlace],
            EndDate = person.DeathDate
        };
        result.Item.ProviderIds.Add(Constants.ProviderName, $"{person.Id}");
        return result;
    }

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(PersonLookupInfo searchInfo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var results = new List<RemoteSearchResult>();

        if (!int.TryParse(searchInfo.ProviderIds.GetOrDefault(Constants.ProviderName), out var id))
            throw new NotImplementedException();

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

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return await api.GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);
    }
}
