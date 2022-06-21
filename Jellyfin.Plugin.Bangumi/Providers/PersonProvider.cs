using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Bangumi.Providers;

public class PersonProvider : IRemoteMetadataProvider<Person, PersonLookupInfo>, IHasOrder
{
    private readonly BangumiApi _api;
    private readonly Plugin _plugin;

    public PersonProvider(Plugin plugin, BangumiApi api)
    {
        _plugin = plugin;
        _api = api;
    }

    public int Order => -5;
    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<Person>> GetMetadata(PersonLookupInfo info, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = new MetadataResult<Person> { ResultLanguage = Constants.Language };
        var personId = info.ProviderIds?.GetValueOrDefault(Constants.ProviderName);
        if (string.IsNullOrEmpty(personId))
            return result;
        var person = await _api.GetPerson(personId, token);
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

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(PersonLookupInfo searchInfo, CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken token)
    {
        return await _plugin.GetHttpClient().GetAsync(url, token).ConfigureAwait(false);
    }
}