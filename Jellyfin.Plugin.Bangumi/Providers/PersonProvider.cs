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

        Model.PersonDetail? person = null;

        var personId = info.ProviderIds?.GetValueOrDefault(Constants.ProviderName);
        string prefix = "";
        if (string.IsNullOrEmpty(personId))
            return result;
        if (personId.StartsWith(Constants.CharacterIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(personId.AsSpan(Constants.CharacterIdPrefix.Length), out var id))
                return result;

            person = await api.GetCharacter(id, cancellationToken);
            prefix = Constants.CharacterIdPrefix;

        }
        else
        {
            if (!int.TryParse(personId, out var id))
                return result;

            person = await api.GetPerson(id, cancellationToken);
        }

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
            EndDate = person.DeathDate,
            ProviderIds = { { Constants.ProviderName, $"{prefix}{person.Id}" } }
        };
        return result;
    }

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(PersonLookupInfo searchInfo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var results = new List<RemoteSearchResult>();

        var personId = searchInfo.ProviderIds.GetOrDefault(Constants.ProviderName);
        if (!string.IsNullOrEmpty(personId))
        {
            Model.PersonDetail? person = null;
            string prefix = "";
            if (personId.StartsWith(Constants.CharacterIdPrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(personId.AsSpan(Constants.CharacterIdPrefix.Length), out var id))
                    return results;

                person = await api.GetCharacter(id, cancellationToken);
                prefix = Constants.CharacterIdPrefix;

            }
            else
            {
                if (!int.TryParse(personId, out var id))
                    return results;

                person = await api.GetPerson(id, cancellationToken);
            }

            if (person == null)
                return results;

            var result = new RemoteSearchResult
            {
                Name = person.Name,
                SearchProviderName = person.Name,
                ImageUrl = person.DefaultImage,
                Overview = person.Summary,
                PremiereDate = person.Birthdate,
                ProviderIds = { { Constants.ProviderName, $"{prefix}{person.Id}" } }
            };

            results.Add(result);
        }
        else
        {
            var searchPersonResult = (await api.SearchPerson(searchInfo.Name, cancellationToken))?.ToList() ?? [];
            var searchCharacterResult = (await api.SearchCharacter(searchInfo.Name, cancellationToken))?.ToList() ?? [];

            results.AddRange(searchPersonResult.Select(item => new RemoteSearchResult
            {
                Name = item.Name,
                SearchProviderName = item.Name,
                ImageUrl = item.DefaultImage,
                Overview = item.ShortSummary,
                ProviderIds = { { Constants.ProviderName, item.Id.ToString() } }
            }));
            results.AddRange(searchCharacterResult.Select(item => new RemoteSearchResult
            {
                Name = item.Name,
                SearchProviderName = item.Name,
                ImageUrl = item.DefaultImage,
                Overview = item.ShortSummary,
                ProviderIds = { { Constants.ProviderName, $"{Constants.CharacterIdPrefix}{item.Id}" } }
            }));
        }


        return results;
    }

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        using var httpClient = api.GetHttpClient();
        return await httpClient.GetAsync(url, cancellationToken);
    }
}
