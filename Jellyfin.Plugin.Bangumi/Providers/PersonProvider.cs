using System;
using System.Collections.Generic;
using System.Linq;
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
        {
            var searchResult = await api.SearchPerson(searchInfo.Name, cancellationToken);
            var persons = searchResult?.ToList();

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

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return await api.GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);
    }
}
